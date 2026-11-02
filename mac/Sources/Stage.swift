import AppKit
import QuartzCore

// Runs every pet on one timer: refreshes the world snapshot, steps and draws
// each pet, spawns ripples under swimmers, and hides everything while a
// full-screen app is in front (unless told not to). Same as src/Stage.cs.
final class Stage: NSObject {
    let world = World()
    var showOverFullscreen = false
    private(set) var pets: [Pet] = []
    private(set) var activity = Activity.levels[Activity.defaultLevel]
    private(set) var scale: Int

    private let petsDir: URL, effectsDir: URL
    private let maxRipples = 6
    private var ripples: [Ripple] = []
    private var rippleSpec: PetSpec?
    private var timer: Timer?
    private var lastTick: CFTimeInterval = 0
    private var hidden = false

    private final class Ripple {
        let window = SpriteWindow()
        var age = -1.0             // seconds since it started, or -1 when idle
        var x = 0.0, y = 0.0
        var frame = -1
    }

    init(petsDir: URL, effectsDir: URL, scale: Int) {
        self.petsDir = petsDir
        self.effectsDir = effectsDir
        self.scale = scale
        super.init()
        world.refresh(dt: 0)
        loadRipple()
        setActivity(Activity.defaultLevel)
    }

    var running: Bool { return timer != nil }
    var levelIndex: Int { return Activity.levels.firstIndex { $0.name == activity.name } ?? Activity.defaultLevel }

    func add(_ name: String) throws {
        let pet = Pet(spec: try PetSpec.load(from: petsDir, name: name, scale: scale), world: world)
        pet.wanderer.avoidOthers = activity.avoidOthers
        pets.append(pet)
        if !hidden { pet.show() }
    }

    func remove(_ name: String) {
        guard let i = pets.firstIndex(where: { $0.spec.name == name }) else { return }
        pets[i].hide()
        pets.remove(at: i)
    }

    func setScale(_ next: Int) throws {
        // Load all first, so a bad file changes nothing.
        let specs = try pets.map { try PetSpec.load(from: petsDir, name: $0.spec.name, scale: next) }
        scale = next
        for (pet, spec) in zip(pets, specs) { pet.setSpec(spec) }
        loadRipple()
    }

    // Apply an Activity level: update rate and which awareness runs.
    func setActivity(_ level: Int) {
        activity = Activity.levels[max(0, min(Activity.levels.count - 1, level))]
        world.windowsEvery = activity.windowsEvery
        world.trackCursor = activity.cursor
        for p in pets { p.wanderer.avoidOthers = activity.avoidOthers }
        if running {
            pause()
            resume()   // restart the timer at the new rate
        }
    }

    func pause() {
        timer?.invalidate()
        timer = nil
    }

    func resume() {
        guard timer == nil else { return }
        lastTick = CACurrentMediaTime()
        let t = Timer(timeInterval: activity.tick, target: self, selector: #selector(tick), userInfo: nil, repeats: true)
        RunLoop.main.add(t, forMode: .common)   // keeps running while the menu is open
        timer = t
    }

    private func loadRipple() {
        rippleSpec = try? PetSpec.load(from: effectsDir, name: "ripple", scale: scale)   // optional
    }

    @objc private func tick() {
        let now = CACurrentMediaTime()
        let dt = min(0.25, now - lastTick)
        lastTick = now
        world.refresh(dt: dt)

        let hide = world.fullscreenApp && !showOverFullscreen
        if hide != hidden {
            hidden = hide
            for p in pets { if hide { p.hide() } else { p.show() } }
            for r in ripples {
                r.window.hide()
                r.age = -1
            }
        }
        if hidden { return }

        let flock = pets.map { $0.wanderer }
        for p in pets {
            p.tick(dt: dt, world: world, flock: flock)
            if p.wanderer.ripple && activity.ripples { spawnRipple(under: p) }
        }
        tickRipples(dt)
    }

    private func spawnRipple(under pet: Pet) {
        guard let spec = rippleSpec else { return }
        var ripple = ripples.first { $0.age < 0 }
        if ripple == nil {
            if ripples.count >= maxRipples { return }
            ripple = Ripple()
            ripples.append(ripple!)
        }
        guard let r = ripple else { return }
        r.age = 0
        r.frame = -1
        r.x = pet.wanderer.rippleX - Double(spec.width) / 2
        r.y = pet.wanderer.rippleY - Double(spec.height) / 2
        r.window.show(spec.image(spec.anims[0], frame: 0, direction: 0), mirrored: false,
                      x: r.x, y: r.y, width: spec.width, height: spec.height)
        r.window.orderFront()
        r.window.place(below: pet.window)
        r.frame = 0
    }

    private func tickRipples(_ dt: Double) {
        guard let spec = rippleSpec else { return }
        let anim = spec.anims[0]
        for r in ripples where r.age >= 0 {
            r.age += dt
            let frame = Int(r.age * anim.fps)
            if frame >= anim.frames {
                r.window.hide()
                r.age = -1
            } else if frame != r.frame {
                r.window.show(spec.image(anim, frame: frame, direction: 0), mirrored: false,
                              x: r.x, y: r.y, width: spec.width, height: spec.height)
                r.frame = frame
            }
        }
    }
}
