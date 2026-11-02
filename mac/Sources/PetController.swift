import AppKit
import QuartzCore

// A borderless, click-through window floating above everything (on every
// Space, including full-screen apps) that shows one pet.
final class PetController: NSObject {
    private(set) var pet: PetSpec
    private let window: NSWindow
    private let sprite = CALayer()
    private let wanderer: Wanderer
    private var timer: Timer?
    private var lastTick: CFTimeInterval = 0
    private var frameClock = 0.0
    private var shown: (anim: String, frame: Int, direction: Int)?
    private var shownOrigin = NSPoint(x: CGFloat.nan, y: CGFloat.nan)

    init(pet: PetSpec) {
        self.pet = pet
        wanderer = Wanderer(area: PetController.roam(pet), pet: pet)

        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: pet.width, height: pet.height),
                              styleMask: .borderless, backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        window.isReleasedWhenClosed = false
        self.window = window
        super.init()

        let view = NSView(frame: NSRect(x: 0, y: 0, width: pet.width, height: pet.height))
        view.wantsLayer = true
        view.layer?.addSublayer(sprite)
        sprite.magnificationFilter = .nearest
        sprite.actions = ["contents": NSNull(), "transform": NSNull(),
                          "bounds": NSNull(), "position": NSNull()]
        window.contentView = view
        resize()
    }

    var running: Bool { return timer != nil }

    func show() {
        draw()
        window.orderFrontRegardless()
        resume()
    }

    func pause() {
        timer?.invalidate()
        timer = nil
    }

    func resume() {
        guard timer == nil else { return }
        lastTick = CACurrentMediaTime()
        let t = Timer(timeInterval: 1.0 / 30, target: self, selector: #selector(tick),
                      userInfo: nil, repeats: true)
        RunLoop.main.add(t, forMode: .common)   // keeps running while the menu is open
        timer = t
    }

    // Swap in a different pet (or the same pet at another size).
    func setPet(_ next: PetSpec) {
        pet = next
        wanderer.configure(pet: next)
        resize()
        draw()
    }

    // The visible area of the main display (minus menu bar and Dock), and the
    // box the pet's top-left corner may move in, measured y-down from its top.
    private static func screenFrame() -> NSRect {
        return (NSScreen.screens.first ?? NSScreen.main)?.visibleFrame ?? NSRect(x: 0, y: 0, width: 800, height: 600)
    }

    private static func roam(_ pet: PetSpec) -> CGRect {
        let frame = screenFrame()
        return CGRect(x: 0, y: 0, width: max(0, frame.width - pet.width), height: max(0, frame.height - pet.height))
    }

    private func resize() {
        let size = NSSize(width: pet.width, height: pet.height)
        window.setContentSize(size)
        window.contentView?.frame = NSRect(origin: .zero, size: size)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        sprite.frame = CGRect(origin: .zero, size: size)
        CATransaction.commit()
        shown = nil
    }

    @objc private func tick() {
        let now = CACurrentMediaTime()
        let dt = min(0.25, now - lastTick)
        lastTick = now
        wanderer.step(dt: dt, area: PetController.roam(pet), size: Double(max(pet.width, pet.height)))
        draw(dt: dt)
    }

    // Pick the frame; change the picture only when it changes, then move.
    private func draw(dt: Double = 0) {
        // The wanderer picks the animation and how fast it plays. Switching
        // animations keeps the frame position, so swim and glide blend.
        let anim = pet.find(wanderer.animation)
        let frame: Int
        if wanderer.oneShot >= 0 {
            frame = min(anim.frames - 1, Int(wanderer.oneShot * Double(anim.frames)))
        } else {
            frameClock = (frameClock + dt * anim.fps * wanderer.animRate)
                .truncatingRemainder(dividingBy: Double(anim.frames))
            frame = min(anim.frames - 1, Int(frameClock))
        }
        let direction = pet.direction(heading: wanderer.heading, facingRight: wanderer.facingRight)

        if shown == nil || shown!.anim != anim.name || shown!.frame != frame || shown!.direction != direction {
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            sprite.contents = pet.image(anim, frame: frame, direction: direction)
            sprite.setAffineTransform(pet.headings == 1 && direction == 1
                ? CGAffineTransform(scaleX: -1, y: 1) : .identity)
            CATransaction.commit()
            shown = (anim: anim.name, frame: frame, direction: direction)
        }

        let screen = PetController.screenFrame()
        let origin = NSPoint(x: (screen.minX + CGFloat(wanderer.x)).rounded(),
                             y: (screen.maxY - CGFloat(wanderer.y) - pet.height).rounded())
        if origin != shownOrigin {
            window.setFrameOrigin(origin)
            shownOrigin = origin
        }
    }
}
