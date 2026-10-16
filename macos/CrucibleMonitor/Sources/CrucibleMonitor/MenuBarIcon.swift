import AppKit

/// The brand flame carries health status; the muted bowl keeps the Crucible silhouette recognizable.
enum MenuBarIcon {
    static func image(healthy: Bool, stopped: Bool, brandingPath: String? = nil) -> NSImage {
        let yellow = frontendYellow(at: brandingPath)
        // Draw as one image: MenuBarExtra does not reliably display arbitrary Shape views in its label.
        // The drawing handler resolves semantic colors in the menu bar's current drawing appearance.
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: true) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            context.saveGState()
            context.scaleBy(x: 18 / 88, y: 18 / 88)
            context.translateBy(x: -6, y: -4)
            context.addPath(bowl)
            context.setFillColor(NSColor.labelColor.withAlphaComponent(0.35).cgColor)
            context.fillPath()
            context.addPath(flames)
            context.setFillColor((stopped ? NSColor.secondaryLabelColor : healthy ? yellow : NSColor.systemOrange).cgColor)
            context.fillPath()
            context.restoreGState()
            return true
        }
        image.isTemplate = false // Preserve the flame color and the bowl's reduced opacity.
        image.accessibilityDescription = stopped ? "Crucible · Stopped" : healthy ? "Crucible · Healthy" : "Crucible · Needs Attention"
        return image
    }

    private static func frontendYellow(at path: String?) -> NSColor {
        // Read the selected frontend runtime's source of truth. Its dark accent is the warm yellow;
        // keep that hue in either menu-bar appearance while the bowl adapts to the background.
        guard let path, let data = FileManager.default.contents(atPath: path),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let branding = json["Branding"] as? [String: Any],
              let themes = branding["Themes"] as? [String: [String: String]],
              let hex = themes["Dark"]?["accent"], hex.hasPrefix("#"), hex.count == 7,
              let rgb = UInt32(hex.dropFirst(), radix: 16) else { return .systemYellow }
        return NSColor(srgbRed: CGFloat((rgb >> 16) & 255) / 255,
                       green: CGFloat((rgb >> 8) & 255) / 255,
                       blue: CGFloat(rgb & 255) / 255, alpha: 1)
    }

    private static var bowl: CGPath {
        // Exact silhouette from wwwroot/assets/brand/crucible/small.svg, in its original coordinates.
        let path = CGMutablePath()
        path.move(to: CGPoint(x: 12, y: 50))
        path.addQuadCurve(to: CGPoint(x: 88, y: 50), control: CGPoint(x: 50, y: 65))
        path.addCurve(to: CGPoint(x: 57, y: 82), control1: CGPoint(x: 82, y: 73), control2: CGPoint(x: 67, y: 80))
        path.addLine(to: CGPoint(x: 63, y: 92))
        path.addLine(to: CGPoint(x: 37, y: 92))
        path.addLine(to: CGPoint(x: 43, y: 82))
        path.addCurve(to: CGPoint(x: 12, y: 50), control1: CGPoint(x: 33, y: 80), control2: CGPoint(x: 18, y: 73))
        path.closeSubpath()
        return path
    }

    private static var flames: CGPath {
        let path = CGMutablePath()
        path.move(to: CGPoint(x: 54, y: 6))
        path.addCurve(to: CGPoint(x: 50, y: 46), control1: CGPoint(x: 71, y: 25), control2: CGPoint(x: 40, y: 30))
        path.addCurve(to: CGPoint(x: 39, y: 19), control1: CGPoint(x: 28, y: 42), control2: CGPoint(x: 28, y: 30))
        path.addCurve(to: CGPoint(x: 54, y: 6), control1: CGPoint(x: 36, y: 35), control2: CGPoint(x: 48, y: 22))
        path.closeSubpath()
        path.move(to: CGPoint(x: 68, y: 27))
        path.addCurve(to: CGPoint(x: 57, y: 48), control1: CGPoint(x: 83, y: 43), control2: CGPoint(x: 67, y: 51))
        path.addCurve(to: CGPoint(x: 68, y: 27), control1: CGPoint(x: 59, y: 36), control2: CGPoint(x: 69, y: 40))
        path.closeSubpath()
        return path
    }
}
