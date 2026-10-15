//! Installed Windows lifecycle. The macOS Monitor remains independent.
use reqwest::blocking::Client;
use serde_json::Value;
use std::{
    env, fs,
    os::windows::{io::AsRawHandle, process::CommandExt},
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    sync::{
        atomic::{AtomicBool, Ordering},
        Mutex,
    },
    thread,
    time::{Duration, Instant},
};
use tauri::{Manager, WebviewUrl, WebviewWindowBuilder, WindowEvent};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons};
use tauri_plugin_opener::OpenerExt;
use windows_sys::Win32::{
    Foundation::{CloseHandle, HANDLE},
    Security::Cryptography::{BCryptGenRandom, BCRYPT_USE_SYSTEM_PREFERRED_RNG},
    System::{
        JobObjects::{
            AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
            SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        },
        Threading::{CreateEventW, SetEvent, CREATE_NO_WINDOW},
    },
};

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(Some(0)).collect()
}
fn secret() -> Result<String, String> {
    let mut bytes = [0u8; 32];
    if unsafe {
        BCryptGenRandom(
            std::ptr::null_mut(),
            bytes.as_mut_ptr(),
            32,
            BCRYPT_USE_SYSTEM_PREFERRED_RNG,
        )
    } != 0
    {
        return Err("Windows could not generate a launcher credential.".into());
    }
    Ok(bytes.iter().map(|b| format!("{b:02x}")).collect())
}
struct Handle(HANDLE);
// Kernel handles are transferable; the lifecycle state is protected by a Mutex.
unsafe impl Send for Handle {}
impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}
impl Handle {
    fn job() -> Result<Self, String> {
        let raw = unsafe { CreateJobObjectW(std::ptr::null(), std::ptr::null()) };
        if raw.is_null() {
            return Err(std::io::Error::last_os_error().to_string());
        }
        let job = Self(raw);
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { std::mem::zeroed() };
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if unsafe {
            SetInformationJobObject(
                raw,
                JobObjectExtendedLimitInformation,
                &info as *const _ as *const _,
                std::mem::size_of_val(&info) as u32,
            )
        } == 0
        {
            return Err(std::io::Error::last_os_error().to_string());
        }
        Ok(job)
    }
}
struct OwnedHost {
    child: Child,
    port: u16,
    role: &'static str,
    owner: String,
    instance: String,
}
struct Session {
    root: PathBuf,
    assets: PathBuf,
    resources: PathBuf,
    job: Handle,
    hosts: Vec<OwnedHost>,
}
struct Lifecycle {
    session: Mutex<Session>,
    closing: AtomicBool,
    exiting: AtomicBool,
}

fn normalized(s: &str) -> String {
        s.trim_start_matches(r"\\?\")
            .replace('/', "\\")
            .trim_end_matches('\\')
            .to_lowercase()
}
fn same_path(a: &str, b: &Path) -> bool {
    normalized(a) == normalized(&b.to_string_lossy())
}
fn overlaps(a: &Path,b: &Path) -> bool {
    let a=normalized(&a.to_string_lossy());let b=normalized(&b.to_string_lossy());
    a==b||a.starts_with(&(b.clone()+"\\"))||b.starts_with(&(a+"\\"))
}
fn health(client: &Client, port: u16, root: &Path, role: &str) -> Result<Option<Value>, String> {
    let response = match client
        .get(format!("http://127.0.0.1:{port}/api/v1/health"))
        .send()
    {
        Ok(r) => r,
        Err(e) if e.is_connect() => return Ok(None),
        Err(e) => return Err(e.to_string()),
    };
    let h: Value = response
        .json()
        .map_err(|_| format!("Port {port} is occupied by another program."))?;
    if h["application"] != "crucible"
        || h["role"] != role
        || !same_path(h["projectRoot"].as_str().unwrap_or(""), root)
    {
        return Err(format!(
            "Port {port} belongs to another host or installation. No process was stopped."
        ));
    }
    if h["runtimeMode"] != "native-aot" {
        return Err(format!(
            "The {role} on port {port} uses a different runtime."
        ));
    }
    Ok(Some(h))
}
fn client() -> Result<Client, String> {
    Client::builder()
        .timeout(Duration::from_secs(3))
        .no_proxy()
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .map_err(|e| e.to_string())
}
impl Session {
    fn logs(&self) -> PathBuf {
        self.root.join("data/desktop/logs")
    }
    fn needs_setup(&self) -> bool {
        let ready=(|| -> Option<bool> {
            let expected=fs::read_to_string(self.assets.join("engine/bridge.sha256")).ok()?;
            let installed: Value=serde_json::from_slice(&fs::read(self.root.join("data/engine-installation.json")).ok()?).ok()?;
            let active: Value=serde_json::from_slice(&fs::read(self.root.join("data/engine-bundles/active.json")).ok()?).ok()?;
            let id=active["id"].as_str()?;
            if id.len()!=129||!id.chars().all(|c|c.is_ascii_hexdigit()||c=='-'){return None;}
            Some(installed["bridgeSourceHash"].as_str()==Some(expected.trim())
                &&self.root.join("vendor/jdk/bin/java.exe").is_file()
                &&self.root.join("vendor/jdk/bin/javac.exe").is_file()
                &&self.root.join("data/engine-bundles").join(id).join("bundle.json").is_file())
        })();
        ready!=Some(true)
    }
    fn spawn(
        &self,
        role: &'static str,
        port: u16,
        backend: u16,
        setup: bool,
    ) -> Result<(Child, String), String> {
        let folder = if role == "service" { "service" } else { "ui" };
        let exe = if role == "service" {
            "CrucibleBackend.exe"
        } else {
            "CrucibleUi.exe"
        };
        let path = self.resources.join(folder).join(exe);
        if !path.is_file() {
            return Err(format!("Reinstall Crucible: {exe} is missing."));
        }
        fs::create_dir_all(self.logs()).map_err(|e| e.to_string())?;
        let log = fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(self.logs().join(if setup { "setup.log" } else { role }))
            .map_err(|e| e.to_string())?;
        let owner = secret()?;
        let event_name = wide(&format!("Local\\Crucible-{}", secret()?));
        let event = Handle(unsafe { CreateEventW(std::ptr::null(), 1, 0, event_name.as_ptr()) });
        if event.0.is_null() {
            return Err(std::io::Error::last_os_error().to_string());
        }
        let mut command = Command::new(&path);
        command
            .current_dir(path.parent().unwrap())
            .creation_flags(CREATE_NO_WINDOW)
            .env("MTG_PROJECT_ROOT", &self.root)
            .env("MTG_ASSET_ROOT", &self.assets)
            .env("MTG_PORT", port.to_string())
            .env("MTG_HOST_MODE", role)
            .env("MTG_BACKEND_URL", format!("http://127.0.0.1:{backend}/"))
            .env("MTG_UI_STATE_ROOT", self.root.join("ui-state"))
            .env("CRUCIBLE_HOST_OWNER", &owner)
            .env(
                "CRUCIBLE_LAUNCH_EVENT",
                String::from_utf16_lossy(&event_name[..event_name.len() - 1]),
            )
            .stdin(Stdio::null())
            .stdout(log.try_clone().map_err(|e| e.to_string())?)
            .stderr(log);
        if setup {
            command.arg("--setup");
        }
        let mut child = command.spawn().map_err(|e| e.to_string())?;
        // The managed entry point waits on this event before creating any child processes.
        if unsafe { AssignProcessToJobObject(self.job.0, child.as_raw_handle() as HANDLE) } == 0 {
            let error = std::io::Error::last_os_error();
            let _ = child.kill();
            let _ = child.wait();
            return Err(format!("Cannot contain the simulation processes: {error}"));
        }
        if unsafe { SetEvent(event.0) } == 0 {
            let _ = child.kill();
            let _ = child.wait();
            return Err("Cannot release the host startup gate.".into());
        }
        Ok((child, owner))
    }
    fn ensure(
        &mut self,
        client: &Client,
        role: &'static str,
        port: u16,
        backend: u16,
    ) -> Result<(), String> {
        if let Some(h) = health(client, port, &self.root, role)? {
            if h["ready"] != true {
                return Err(format!("The existing {role} is not ready. See its logs."));
            }
            return Ok(()); // Observed, never added to the owned process list.
        }
        let (child, owner) = self.spawn(role, port, backend, false)?;
        self.hosts.push(OwnedHost {
            child,
            port,
            role,
            owner,
            instance: String::new(),
        });
        let index = self.hosts.len() - 1;
        let deadline = Instant::now() + Duration::from_secs(90);
        while Instant::now() < deadline {
            if let Some(status) = self.hosts[index]
                .child
                .try_wait()
                .map_err(|e| e.to_string())?
            {
                return Err(format!(
                    "{role} exited ({status}). Open the logs for details."
                ));
            }
            if let Some(h) = health(client, port, &self.root, role)? {
                if h["processId"].as_u64() != Some(self.hosts[index].child.id() as u64) {
                    return Err(
                        "Another host won the startup race. No external process was stopped."
                            .into(),
                    );
                }
                if h["ready"] == true {
                    self.hosts[index].instance = h["instanceId"].as_str().unwrap_or("").into();
                    return Ok(());
                }
            }
            thread::sleep(Duration::from_millis(150));
        }
        Err(format!(
            "{role} startup timed out. Open the logs for details."
        ))
    }
    fn active(&mut self, client: &Client) -> Result<bool, String> {
        for host in &mut self.hosts {
            if host.child.try_wait().map_err(|e| e.to_string())?.is_some() {
                continue;
            }
            let h = health(client, host.port, &self.root, host.role)?
                .ok_or("An owned host stopped responding. Open the logs before exiting.")?;
            if h["instanceId"].as_str() != Some(host.instance.as_str()) {
                return Err("Host ownership changed. The replacement will not be stopped.".into());
            }
            if [
                "activeRuns",
                "queuedRuns",
                "activeOptimizations",
                "queuedOptimizations",
            ]
            .iter()
            .any(|k| h[k].as_u64().unwrap_or(0) > 0)
            {
                return Ok(true);
            }
        }
        Ok(false)
    }
    fn stop(&mut self, client: &Client, stop_work: bool) -> Result<(), String> {
        // Stop backend first, so completed replays and run states are committed before UI exit.
        for host in &mut self.hosts {
            if host.child.try_wait().map_err(|e| e.to_string())?.is_some() {
                continue;
            }
            let h = health(client, host.port, &self.root, host.role)?
                .ok_or("An owned host is unavailable.")?;
            if h["instanceId"].as_str() != Some(host.instance.as_str()) {
                return Err("Host identity changed; refusing shutdown.".into());
            }
            let session: Value = client
                .get(format!("http://127.0.0.1:{}/api/v1/session", host.port))
                .send()
                .and_then(|r| r.error_for_status())
                .and_then(|r| r.json())
                .map_err(|e| e.to_string())?;
            client
                .post(format!(
                    "http://127.0.0.1:{}/api/v1/host/shutdown?stopWork={stop_work}",
                    host.port
                ))
                .header(
                    "X-Instance-Token",
                    session["instanceToken"].as_str().unwrap_or(""),
                )
                .header("X-Crucible-Owner", &host.owner)
                .send()
                .and_then(|r| r.error_for_status())
                .map_err(|e| format!("Could not stop {} safely: {e}", host.role))?;
            let deadline = Instant::now() + Duration::from_secs(30);
            while host.child.try_wait().map_err(|e| e.to_string())?.is_none() {
                if Instant::now() > deadline {
                    return Err("Shutdown is taking longer than expected. Keep Crucible open and inspect its logs.".into());
                }
                thread::sleep(Duration::from_millis(100));
            }
        }
        self.hosts.clear();
        Ok(())
    }
    fn discard_failed_start(&mut self) {
        // Only processes created by this launcher can be here. Setup has not exposed the run UI yet.
        for host in &mut self.hosts {
            let _ = host.child.kill();
            let _ = host.child.wait();
        }
        self.hosts.clear();
    }
}
fn progress(app: &tauri::AppHandle, message: &str) {
    if let Some(w) = app.get_webview_window("setup") {
        let _ = w.eval(format!(
            "document.getElementById('status').textContent={}",
            serde_json::to_string(message).unwrap()
        ));
    }
}
fn open_logs(app: &tauri::AppHandle) {
    let state = app.state::<Lifecycle>();
    if let Ok(s) = state.session.lock() {
        let _ = app
            .opener()
            .open_path(s.logs().to_string_lossy(), None::<&str>);
    };
}
fn bootstrap(app: &tauri::AppHandle) -> Result<(), String> {
    let client = client()?;
    let backend = super::port("CRUCIBLE_BACKEND_PORT", 5079)?;
    let ui = super::port("CRUCIBLE_UI_PORT", 5077)?;
    if ui == backend {
        return Err("UI and backend ports must be different.".into());
    }
    let state = app.state::<Lifecycle>();
    let mut session = state.session.lock().map_err(|e| e.to_string())?;
    // Check BOTH ports before downloads or spawning anything.
    for (port, role) in [(backend, "service"), (ui, "ui")] {
        health(&client, port, &session.root, role)?;
    }
    let existing_only = env::args().any(|a| a == "--existing-hosts-only");
    if existing_only {
        for (port, role) in [(backend, "service"), (ui, "ui")] {
            if health(&client, port, &session.root, role)?.is_none_or(|h| h["ready"] != true) {
                return Err(format!("Start the existing {role} first."));
            }
        }
    } else {
        if health(&client, backend, &session.root, "service")?.is_none()
            && session.needs_setup()
        {
            progress(app,"Preparing the card catalog and simulation engine. The first download may take several minutes.");
            let (mut setup, _) = session.spawn("service", backend, backend, true)?;
            let setup_log = session.logs().join("setup.log");
            drop(session);
            let deadline = Instant::now() + Duration::from_secs(1800);
            loop {
                if let Some(status) = setup.try_wait().map_err(|e| e.to_string())? {
                    if !status.success() {
                        return Err(
                            "Engine setup failed. Retry Setup or open the logs for details.".into(),
                        );
                    }
                    break;
                }
                if Instant::now() > deadline {
                    let _ = setup.kill();
                    let _ = setup.wait();
                    return Err(
                        "Engine setup timed out. Retry when your connection is available.".into(),
                    );
                }
                if let Ok(log) = fs::read_to_string(&setup_log) {
                    if let Some(line) = log.lines().rev().find(|line| {
                        line.starts_with("Downloading")
                            || line.starts_with("Compiling")
                            || line.starts_with("Setup complete")
                    }) {
                        progress(app, line);
                    }
                }
                thread::sleep(Duration::from_millis(400));
            }
            session = state.session.lock().map_err(|e| e.to_string())?;
        }
        progress(app, "Starting Crucible…");
        session.ensure(&client, "service", backend, backend)?;
        session.ensure(&client, "ui", ui, backend)?;
    }
    drop(session);
    let opener = app.clone();
    WebviewWindowBuilder::new(
        app,
        "main",
        WebviewUrl::External(format!("http://127.0.0.1:{ui}/").parse().unwrap()),
    )
    .title("Crucible · Windows Beta")
    .inner_size(1360.0, 900.0)
    .min_inner_size(390.0, 600.0)
    .on_new_window(move |url, _| {
        if matches!(url.scheme(), "http" | "https")
            && url.username().is_empty()
            && url.password().is_none()
        {
            let _ = opener.opener().open_url(url.as_str(), None::<&str>);
        }
        tauri::webview::NewWindowResponse::Deny
    })
    .on_navigation(move |url| {
        url.scheme() == "http" && url.host_str() == Some("127.0.0.1") && url.port() == Some(ui)
    })
    .build()
    .map_err(|e| e.to_string())?;
    if let Some(window) = app.get_webview_window("setup") {
        let _ = window.destroy();
    }
    Ok(())
}
fn finish(app: &tauri::AppHandle, code: i32) {
    app.state::<Lifecycle>()
        .exiting
        .store(true, Ordering::SeqCst);
    app.exit(code);
}
fn close(app: tauri::AppHandle) {
    let state = app.state::<Lifecycle>();
    if state.closing.swap(true, Ordering::SeqCst) {
        return;
    }
    thread::spawn(move || {
        let result = (|| -> Result<(), String> {
            if app.get_webview_window("main").is_none() {
                if app
                    .dialog()
                    .message(
                        "You can retry setup when you reopen Crucible. Saved data will be kept.",
                    )
                    .title("Stop Setup And Exit?")
                    .buttons(MessageDialogButtons::OkCancelCustom(
                        "Stop And Exit".into(),
                        "Keep Open".into(),
                    ))
                    .blocking_show()
                {
                    finish(&app, 0);
                }
                return Ok(());
            }
            let c = client()?;
            let busy = app
                .state::<Lifecycle>()
                .session
                .lock()
                .map_err(|e| e.to_string())?
                .active(&c)?;
            if busy&&!app.dialog().message("Stopping now preserves completed games. Unfinished games will need explicit resume when you reopen Crucible.")
                .title("Stop Simulations And Exit?")
                .buttons(MessageDialogButtons::OkCancelCustom("Stop And Exit".into(),"Keep Open".into())).blocking_show() {return Ok(());}
            app.state::<Lifecycle>()
                .session
                .lock()
                .map_err(|e| e.to_string())?
                .stop(&c, busy)?;
            finish(&app, 0);
            Ok(())
        })();
        if let Err(error) = result {
            if app
                .dialog()
                .message(error)
                .title("Crucible Is Still Open")
                .buttons(MessageDialogButtons::OkCancelCustom(
                    "Open Logs".into(),
                    "Keep Open".into(),
                ))
                .blocking_show()
            {
                open_logs(&app);
            }
        }
        app.state::<Lifecycle>()
            .closing
            .store(false, Ordering::SeqCst);
    });
}
pub fn run() {
    if env::args().any(|a| a == "--check-runtime") {
        println!("Crucible desktop: native Tauri shell");
        return;
    }
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            if let Some(w) = app
                .get_webview_window("main")
                .or_else(|| app.get_webview_window("setup"))
            {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .setup(|app| {
            let resources = app.path().resource_dir()?.join("resources");
            let assets = resources.join("assets");
            if !assets.join("starter/manifest.json").is_file() {
                return Err("Installation assets are missing. Reinstall Crucible.".into());
            }
            let root = env::var_os("MTG_PROJECT_ROOT")
                .map(PathBuf::from)
                .unwrap_or(app.path().local_data_dir()?.join("Crucible"));
            fs::create_dir_all(&root)?;
            let root = fs::canonicalize(root)?;
            let install=fs::canonicalize(resources.parent().ok_or("Installation folder is unavailable.")?)?;
            if overlaps(&root,&install) {return Err("Keep Crucible's user-data folder separate from its program folder. Choose a different installation folder or MTG_PROJECT_ROOT.".into());}
            app.manage(Lifecycle {
                session: Mutex::new(Session {
                    root,
                    assets,
                    resources,
                    job: Handle::job().map_err(std::io::Error::other)?,
                    hosts: vec![],
                }),
                closing: AtomicBool::new(false),
                exiting: AtomicBool::new(false),
            });
            WebviewWindowBuilder::new(app, "setup", WebviewUrl::App("setup.html".into()))
                .title("Crucible Setup")
                .inner_size(600.0, 340.0)
                .resizable(false)
                .build()?;
            let handle = app.handle().clone();
            thread::spawn(move || loop {
                match bootstrap(&handle) {
                    Ok(()) => break,
                    Err(error) => {
                        if let Ok(mut s) = handle.state::<Lifecycle>().session.lock() {
                            s.discard_failed_start();
                        }
                        progress(&handle, &error);
                        if !handle
                            .dialog()
                            .message(format!(
                                "{error}\n\nSetup can be retried without removing saved data."
                            ))
                            .title("Crucible Could Not Start")
                            .buttons(MessageDialogButtons::OkCancelCustom(
                                "Retry Setup".into(),
                                "Open Logs And Exit".into(),
                            ))
                            .blocking_show()
                        {
                            open_logs(&handle);
                            finish(&handle, 1);
                            break;
                        }
                    }
                }
            });
            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                close(window.app_handle().clone());
            }
        })
        .build(tauri::generate_context!());
    match app {
        Ok(app) => app.run(|app, event| {
            if let tauri::RunEvent::ExitRequested { api, .. } = event {
                if !app.state::<Lifecycle>().exiting.load(Ordering::SeqCst) {
                    api.prevent_exit();
                    close(app.clone());
                }
            }
        }),
        Err(error) => {
            // Even errors before the event loop have a visible native dialog (release has no console).
            eprintln!("Crucible could not start: {error}");
            use windows_sys::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK};
            unsafe {
                MessageBoxW(std::ptr::null_mut(),wide(&format!("Crucible could not start: {error}\nReinstall the app or check access to its data folder.")).as_ptr(),wide("Crucible").as_ptr(),MB_OK|MB_ICONERROR);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn canonical_windows_paths() {
        assert!(same_path(
            r"C:\Users\Name\Crucible",
            Path::new(r"\\?\C:\Users\Name\Crucible")
        ));
    }
    #[test]
    fn unique_credentials() {
        assert_ne!(secret().unwrap(), secret().unwrap());
        assert_eq!(secret().unwrap().len(), 64);
    }
    #[test]
    fn job_and_launch_gate_can_be_created() {
        let _job = Handle::job().unwrap();
    }

    #[test]
    #[ignore]
    fn sleep_fixture() {
        if env::var("CRUCIBLE_TEST_SLEEP").as_deref()==Ok("1") {thread::sleep(Duration::from_secs(30));}
    }

    #[test]
    fn closing_job_stops_only_owned_processes() {
        let spawn=||Command::new(env::current_exe().unwrap())
            .args(["--exact","windows::tests::sleep_fixture","--ignored"])
            .env("CRUCIBLE_TEST_SLEEP","1").creation_flags(CREATE_NO_WINDOW).spawn().unwrap();
        let mut owned=spawn();let mut external=spawn();
        let job=Handle::job().unwrap();
        assert_ne!(unsafe{AssignProcessToJobObject(job.0,owned.as_raw_handle() as HANDLE)},0);
        drop(job);
        let deadline=Instant::now()+Duration::from_secs(5);
        while owned.try_wait().unwrap().is_none()&&Instant::now()<deadline {thread::sleep(Duration::from_millis(20));}
        let stopped=owned.try_wait().unwrap().is_some();
        let external_alive=external.try_wait().unwrap().is_none();
        let _=owned.kill();let _=owned.wait();let _=external.kill();let _=external.wait();
        assert!(stopped,"Closing the job must stop its owned child.");
        assert!(external_alive,"An unassigned process must remain alive.");
    }
}
