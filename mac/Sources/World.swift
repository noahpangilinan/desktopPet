import AppKit
import CoreGraphics

// Something a bird can stand on: the bottom of a screen's visible area (the
// top of the Dock) or a visible stretch of a window's top edge.
struct Surface {
    var id: Int             // negative for ground, else the window number
    var left: Double        // the standing span
    var right: Double
    var top: Double         // y of the line feet rest on
    var ground: Bool { return id < 0 }
}

// A snapshot of the desktop the pets live on, refreshed by the stage: the
// screens, surfaces to perch on, the cursor, and whether a full-screen app is
// in front. Everything is in global display coordinates with y running down
// from the top of the main screen (Quartz's convention, matching Windows).
final class World {
    var workAreas: [CGRect] = []
    var surfaces: [Surface] = []
    var cursorX = 0.0, cursorY = 0.0
    var cursorSpeed = 0.0          // points per second, smoothed
    var cursorIdle = 0.0           // seconds the cursor has been still
    var fullscreenApp = false

    // Set from the Activity level.
    var windowsEvery = 0.5         // seconds between window scans; 0 = Dock only
    var trackCursor = true

    private let screensEvery = 2.0, fullscreenEvery = 2.0
    private let minSpan = 100.0    // narrower stretches aren't worth landing on
    private let ownPID = ProcessInfo.processInfo.processIdentifier
    private var windowTops: [Surface] = []
    private var screenFrames: [CGRect] = []
    private var screensClock = Double.greatestFiniteMagnitude
    private var windowsClock = Double.greatestFiniteMagnitude
    private var cursorKnown = false

    // Height of the main screen, for flipping between AppKit's y-up and ours.
    static var flipHeight: CGFloat {
        return NSScreen.screens.first?.frame.height ?? 0
    }

    func refresh(dt: Double) {
        if !trackCursor {
            cursorKnown = false
            cursorSpeed = 0
            cursorIdle = 0
        } else {
            let p = NSEvent.mouseLocation
            let x = Double(p.x), y = Double(World.flipHeight - p.y)
            if cursorKnown && dt > 0 {
                let instant = ((x - cursorX) * (x - cursorX) + (y - cursorY) * (y - cursorY)).squareRoot() / dt
                cursorSpeed = cursorSpeed * 0.6 + instant * 0.4
                cursorIdle = instant < 20 ? cursorIdle + dt : 0
            }
            cursorX = x
            cursorY = y
            cursorKnown = true
        }

        screensClock += dt
        if screensClock >= screensEvery {
            screensClock = 0
            let h = World.flipHeight
            func flip(_ r: CGRect) -> CGRect {
                return CGRect(x: r.minX, y: h - r.maxY, width: r.width, height: r.height)
            }
            workAreas = NSScreen.screens.map { flip($0.visibleFrame) }
            screenFrames = NSScreen.screens.map { flip($0.frame) }
        }

        windowsClock += dt
        if windowsClock >= (windowsEvery > 0 ? windowsEvery : fullscreenEvery) {
            windowsClock = 0
            scanWindows(perch: windowsEvery > 0)
        }

        surfaces = workAreas.enumerated().map { i, w in
            Surface(id: -(i + 1), left: Double(w.minX), right: Double(w.maxX), top: Double(w.maxY))
        }
        surfaces.append(contentsOf: windowTops)
    }

    // The surface with this id whose span still covers left...right, or nil.
    func find(id: Int, left: Double, right: Double) -> Surface? {
        return surfaces.first { $0.id == id && $0.left <= left + 1 && $0.right >= right - 1 }
    }

    // The ground (Dock top) under a point's screen.
    func groundAt(x: Double, y: Double) -> Surface? {
        guard let i = screenIndex(x: x, y: y), i < surfaces.count else { return nil }
        return surfaces[i]
    }

    // The visible area containing a point, or the nearest one.
    func workAt(x: Double, y: Double) -> CGRect {
        guard let i = screenIndex(x: x, y: y) else { return CGRect(x: 0, y: 0, width: 800, height: 600) }
        return workAreas[i]
    }

    private func screenIndex(x: Double, y: Double) -> Int? {
        var best: Int?
        var bestGap = Double.greatestFiniteMagnitude
        for (i, w) in workAreas.enumerated() {
            let gx = max(0, max(Double(w.minX) - x, x - Double(w.maxX)))
            let gy = max(0, max(Double(w.minY) - y, y - Double(w.maxY)))
            let gap = gx * gx + gy * gy
            if gap < bestGap { bestGap = gap; best = i }
        }
        return best
    }

    // True if a point is inside any screen's visible area.
    func onAnyScreen(x: Double, y: Double) -> Bool {
        return workAreas.contains { Double($0.minX) <= x && x < Double($0.maxX) && Double($0.minY) <= y && y < Double($0.maxY) }
    }

    // Walk the windows front to back, then keep the stretches of each window's
    // top edge that nothing in front covers. Also spots a full-screen app.
    private func scanWindows(perch: Bool) {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let info = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
            windowTops = []
            fullscreenApp = false
            return
        }
        var windows: [(id: Int, pid: Int32, rect: CGRect)] = []
        for w in info {
            guard (w[kCGWindowLayer as String] as? Int) == 0,
                  let pid = w[kCGWindowOwnerPID as String] as? Int32, pid != ownPID,
                  let number = w[kCGWindowNumber as String] as? Int,
                  let bounds = w[kCGWindowBounds as String] as? NSDictionary,
                  let rect = CGRect(dictionaryRepresentation: bounds),
                  rect.width > 0, rect.height > 0 else { continue }
            if let alpha = w[kCGWindowAlpha as String] as? Double, alpha <= 0 { continue }
            windows.append((number, pid, rect))
        }

        // Full screen: the front app's frontmost window covers a whole screen.
        let front = NSWorkspace.shared.frontmostApplication?.processIdentifier
        if let top = windows.first(where: { $0.pid == front }) {
            fullscreenApp = screenFrames.contains { top.rect.contains($0) }
        } else {
            fullscreenApp = false
        }

        windowTops = []
        guard perch else { return }
        for (i, w) in windows.enumerated() {
            let r = w.rect
            guard Double(r.width) >= minSpan, r.height >= 60 else { continue }
            let work = workAt(x: Double(r.midX), y: Double(r.minY) + 1)
            guard r.minY >= work.minY + 40, r.minY < work.maxY else { continue }   // room above
            var spans: [(Double, Double)] = [(Double(max(r.minX, work.minX)), Double(min(r.maxX, work.maxX)))]
            for f in windows[..<i] where f.rect.minY <= r.minY + 1 && f.rect.maxY > r.minY - 4 {
                spans = World.subtract(spans, Double(f.rect.minX), Double(f.rect.maxX))
            }
            for s in spans where s.1 - s.0 >= minSpan {
                windowTops.append(Surface(id: w.id, left: s.0, right: s.1, top: Double(r.minY)))
            }
        }
    }

    private static func subtract(_ spans: [(Double, Double)], _ left: Double, _ right: Double) -> [(Double, Double)] {
        var result: [(Double, Double)] = []
        for s in spans {
            if right <= s.0 || left >= s.1 { result.append(s); continue }
            if left > s.0 { result.append((s.0, left)) }
            if right < s.1 { result.append((right, s.1)) }
        }
        return result
    }
}
