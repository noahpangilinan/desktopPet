import AppKit

// One animal on screen: its sprites, its movement, and its window.
final class Pet {
    private(set) var spec: PetSpec
    let wanderer: Wanderer
    let window = SpriteWindow()

    private var frameClock = 0.0
    private var shown: (anim: String, frame: Int, direction: Int)?

    init(spec: PetSpec, world: World) {
        self.spec = spec
        wanderer = Wanderer(world: world, pet: spec)
    }

    func show() {
        shown = nil
        draw(dt: 0)
        window.orderFront()
    }

    func hide() { window.hide() }

    // Swap in new sprites (another size).
    func setSpec(_ next: PetSpec) {
        spec = next
        wanderer.configure(pet: next)
        shown = nil
        if window.visible { draw(dt: 0) }
    }

    func tick(dt: Double, world: World, flock: [Wanderer]) {
        wanderer.step(dt: dt, world: world, flock: flock)
        draw(dt: dt)
    }

    // Pick the frame the wanderer asks for; change the picture only when it
    // changes, otherwise just move the window.
    private func draw(dt: Double) {
        let anim = spec.find(wanderer.animation)
        let frame: Int
        if wanderer.oneShot >= 0 {
            frame = min(anim.frames - 1, Int(wanderer.oneShot * Double(anim.frames)))
        } else {
            // Switching animations keeps the frame position, so swim and glide blend.
            frameClock = (frameClock + dt * anim.fps * wanderer.animRate)
                .truncatingRemainder(dividingBy: Double(anim.frames))
            frame = min(anim.frames - 1, Int(frameClock))
        }
        let direction = spec.direction(heading: wanderer.heading, facingRight: wanderer.facingRight)

        if let s = shown, s.anim == anim.name, s.frame == frame, s.direction == direction {
            window.move(x: wanderer.x, y: wanderer.y)
        } else {
            window.show(spec.image(anim, frame: frame, direction: direction),
                        mirrored: spec.headings == 1 && direction == 1,
                        x: wanderer.x, y: wanderer.y, width: spec.width, height: spec.height)
            shown = (anim: anim.name, frame: frame, direction: direction)
        }
    }
}
