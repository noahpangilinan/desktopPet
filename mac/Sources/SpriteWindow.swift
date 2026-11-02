import AppKit
import QuartzCore

// A borderless, click-through window floating above everything (on every
// Space, including full-screen apps) that shows one image. Used for pets and
// for ripples. Positions are the World's y-down coordinates.
final class SpriteWindow {
    let window: NSWindow
    private let sprite = CALayer()
    private var shownOrigin = NSPoint(x: CGFloat.nan, y: CGFloat.nan)
    private var size = NSSize.zero

    init() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1, height: 1),
                          styleMask: .borderless, backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        window.isReleasedWhenClosed = false

        let view = NSView(frame: NSRect(x: 0, y: 0, width: 1, height: 1))
        view.wantsLayer = true
        view.layer?.addSublayer(sprite)
        sprite.magnificationFilter = .nearest
        sprite.actions = ["contents": NSNull(), "transform": NSNull(),
                          "bounds": NSNull(), "position": NSNull()]
        window.contentView = view
    }

    var visible: Bool { return window.isVisible }

    // Show an image (mirrored for side-on pets facing left) at a y-down position.
    func show(_ image: CGImage?, mirrored: Bool, x: Double, y: Double, width: CGFloat, height: CGFloat) {
        let newSize = NSSize(width: width, height: height)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        if newSize != size {
            size = newSize
            window.setContentSize(newSize)
            window.contentView?.frame = NSRect(origin: .zero, size: newSize)
            sprite.frame = CGRect(origin: .zero, size: newSize)
            shownOrigin = NSPoint(x: CGFloat.nan, y: CGFloat.nan)
        }
        sprite.contents = image
        sprite.setAffineTransform(mirrored ? CGAffineTransform(scaleX: -1, y: 1) : .identity)
        CATransaction.commit()
        move(x: x, y: y)
    }

    // Move without changing the picture.
    func move(x: Double, y: Double) {
        let origin = NSPoint(x: CGFloat(x).rounded(),
                             y: (World.flipHeight - CGFloat(y) - size.height).rounded())
        if origin != shownOrigin {
            window.setFrameOrigin(origin)
            shownOrigin = origin
        }
    }

    func orderFront() { window.orderFrontRegardless() }
    func hide() { window.orderOut(nil) }

    // Directly behind another of our windows (ripples go under their fish).
    func place(below other: SpriteWindow) {
        window.order(.below, relativeTo: other.window.windowNumber)
    }
}
