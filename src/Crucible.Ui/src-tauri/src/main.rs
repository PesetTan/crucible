#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#[cfg(not(windows))]
use serde_json::Value;
use std::env;
#[cfg(not(windows))]
use std::{
    fs,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    thread,
    time::{Duration, Instant},
};
#[cfg(not(windows))]
use tauri::{Manager, WebviewUrl, WebviewWindowBuilder, WindowEvent};
#[cfg(not(windows))]
use tauri_plugin_opener::OpenerExt;

#[cfg(windows)]
mod windows;
#[cfg(windows)]
fn main() {
    windows::run();
}

fn port(name: &str, default: u16) -> Result<u16, String> {
    env::var(name)
        .ok()
        .map(|s| s.parse::<u16>().map_err(|_| format!("Invalid {name}")))
        .unwrap_or(Ok(default))
        .and_then(|n| {
            if n >= 1024 {
                Ok(n)
            } else {
                Err(format!("Invalid {name}"))
            }
        })
}
#[cfg(not(windows))]
fn probe(
    client: &reqwest::blocking::Client,
    port: u16,
    root: &Path,
    role: &str,
) -> Result<Option<Value>, String> {
    let response = match client
        .get(format!("http://127.0.0.1:{port}/api/v1/health"))
        .send()
    {
        Ok(r) => r,
        Err(e) if e.is_connect() => return Ok(None),
        Err(e) => return Err(format!("Health check failed on {port}: {e}")),
    };
    let h: Value = response
        .json()
        .map_err(|_| format!("Port {port} is occupied by an unrecognized service."))?;
    if h["application"] != "crucible"
        || h["role"] != role
        || h["projectRoot"].as_str() != root.to_str()
    {
        return Err(format!(
            "Port {port} belongs to another host or project. No process was stopped."
        ));
    }
    if h["ready"] != true {
        return Err(format!(
            "Crucible {role} is already running but not ready. Inspect it in Crucible Monitor."
        ));
    }
    if h["runtimeMode"] != "native-aot" {
        return Err(format!("The current {role} uses the previous runtime. Switch it through Crucible Monitor before opening this build."));
    }
    Ok(Some(h))
}
#[cfg(not(windows))]
fn ensure(
    client: &reqwest::blocking::Client,
    port: u16,
    backend_port: u16,
    root: &Path,
    resources: &Path,
    role: &str,
) -> Result<(), String> {
    if probe(client, port, root, role)?.is_some() {
        return Ok(());
    }
    let (folder, exe) = if role == "ui" {
        ("ui", "CrucibleUi")
    } else {
        ("service", "CrucibleBackend")
    };
    let executable = resources
        .join("resources")
        .join(folder)
        .join(if cfg!(windows) {
            format!("{exe}.exe")
        } else {
            exe.to_owned()
        });
    if !executable.is_file() {
        return Err(format!(
            "Bundled native host is missing: {}",
            executable.display()
        ));
    }
    let logs = root.join("data/desktop/logs");
    fs::create_dir_all(&logs).map_err(|e| e.to_string())?;
    let log = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(logs.join(format!("{role}.log")))
        .map_err(|e| e.to_string())?;
    let mut command = Command::new(&executable);
    command
        .current_dir(executable.parent().unwrap())
        .env("MTG_PROJECT_ROOT", root)
        .env("MTG_HOST_MODE", if role == "ui" { "ui" } else { "service" })
        .env("MTG_PORT", port.to_string())
        .env(
            "MTG_BACKEND_URL",
            format!("http://127.0.0.1:{backend_port}/"),
        )
        .env("MTG_UI_STATE_ROOT", root.join("src/DeckTester/ui-state"))
        .stdin(Stdio::null())
        .stdout(log.try_clone().map_err(|e| e.to_string())?)
        .stderr(log);
    let java = if cfg!(target_os = "macos") {
        root.join("vendor/jdk/Contents/Home/bin/java")
    } else {
        root.join(if cfg!(windows) {
            "vendor/jdk/bin/java.exe"
        } else {
            "vendor/jdk/bin/java"
        })
    };
    if java.is_file() {
        command.env("MTG_JAVA", java);
    }
    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        command.process_group(0);
    }
    let mut child = command
        .spawn()
        .map_err(|e| format!("Cannot launch {role}: {e}"))?;
    let deadline = Instant::now() + Duration::from_secs(60);
    while Instant::now() < deadline {
        if let Some(status) = child.try_wait().map_err(|e| e.to_string())? {
            return Err(format!(
                "Native {role} exited ({status}). See {}",
                logs.display()
            ));
        }
        if let Ok(Some(_)) = probe(client, port, root, role) {
            // Reap when it eventually exits, but never terminate a host just because its UI closes.
            thread::spawn(move || {
                let _ = child.wait();
            });
            return Ok(());
        }
        thread::sleep(Duration::from_millis(150));
    }
    Err(format!(
        "Native {role} did not become ready. It was left running for inspection; see {}",
        logs.display()
    ))
}
#[cfg(not(windows))]
fn root(resources: &Path) -> Result<PathBuf, String> {
    let candidate = env::var_os("MTG_PROJECT_ROOT")
        .map(PathBuf::from)
        .or_else(|| {
            fs::read_to_string(resources.join("resources/project-root.txt"))
                .ok()
                .map(|s| PathBuf::from(s.trim()))
        })
        .ok_or("Set MTG_PROJECT_ROOT to your Crucible installation folder.")?;
    let root =
        fs::canonicalize(candidate).map_err(|e| format!("Project folder is unavailable: {e}"))?;
    if !root.join("docs/specs").is_dir() {
        return Err("This folder is not a Crucible installation.".into());
    }
    Ok(root)
}
#[cfg(not(windows))]
fn main() {
    let existing_hosts_only = env::args().any(|a| a == "--existing-hosts-only");
    let diagnostics = env::args().any(|a| a == "--check-runtime");
    if diagnostics {
        println!("Crucible desktop: native Tauri shell");
        return;
    }
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .setup(move |app| {
            let resources = app.path().resource_dir()?;
            let root = root(&resources).map_err(std::io::Error::other)?;
            let ui_port = port("CRUCIBLE_UI_PORT", 5077).map_err(std::io::Error::other)?;
            let backend_port =
                port("CRUCIBLE_BACKEND_PORT", 5079).map_err(std::io::Error::other)?;
            let client = reqwest::blocking::Client::builder()
                .timeout(Duration::from_secs(2))
                .redirect(reqwest::redirect::Policy::none())
                .no_proxy()
                .build()?;
            if existing_hosts_only {
                for (port, role) in [(backend_port, "service"), (ui_port, "ui")] {
                    if probe(&client, port, &root, role)
                        .map_err(std::io::Error::other)?
                        .is_none()
                    {
                        return Err(std::io::Error::other(format!(
                            "Crucible {role} is unavailable. Start it in Crucible Monitor."
                        ))
                        .into());
                    }
                }
            } else {
                ensure(
                    &client,
                    backend_port,
                    backend_port,
                    &root,
                    &resources,
                    "service",
                )
                .map_err(std::io::Error::other)?;
                ensure(&client, ui_port, backend_port, &root, &resources, "ui")
                    .map_err(std::io::Error::other)?;
            }
            let opener = app.handle().clone();
            WebviewWindowBuilder::new(
                app,
                "main",
                WebviewUrl::External(format!("http://127.0.0.1:{ui_port}/").parse()?),
            )
            .title("Crucible")
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
                url.scheme() == "http"
                    && url.host_str() == Some("127.0.0.1")
                    && url.port() == Some(ui_port)
            })
            .build()?;
            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .build(tauri::generate_context!());
    match app {
        Ok(app) => app.run(|app, event| {
            // A regular Mac application reopens its hidden window from the Dock or Monitor.
            #[cfg(target_os = "macos")]
            if let tauri::RunEvent::Reopen { .. } = event {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.set_focus();
                }
            }
        }),
        Err(error) => {
            eprintln!("Crucible could not open: {error}");
            std::process::exit(1)
        }
    }
}
