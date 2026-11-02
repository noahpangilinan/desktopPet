import Foundation

// Moves a pet around the desktop the way an animal would: it keeps a heading
// and can only turn so fast, so paths curve; a slow wobble makes them meander;
// screen edges with no neighbouring screen push it back. Pets with a glide
// animation swim in bursts (a few strong tail beats) followed by coasting,
// and sometimes rest. Swimmers are moody about the cursor: a swipe always
// scares them, and when it comes near they're either spooked or hungry,
// swimming up to nibble it a few times like fish taking food. Pets steer
// around each other.
//
// Side-on pets with land, stand and takeoff animations (birds) also perch:
// every so often they fly down to the Dock or the top edge of a window, land,
// stand around hopping and turning, then take off again. They take off early
// if the cursor comes close or their window moves or is covered.
//
// Same behaviour as src/Wanderer.cs, in the World's y-down coordinates.
final class Wanderer {
    var x = 0.0, y = 0.0              // top-left of the pet
    var width = 0.0, height = 0.0
    var heading = 0.0                 // radians, 0 = right, clockwise
    var facingRight = true            // for side-on pets
    var animation: String?            // nil = the pet's first animation
    var animRate = 1.0                // multiplier on the animation's fps
    var oneShot = -1.0                // 0...1 through a play-once animation, or -1 to loop
    var ripple = false                // true for one step when a swimmer disturbs the water
    var rippleX = 0.0, rippleY = 0.0  // where, when ripple is set
    var avoidOthers = true            // steer around other pets (Activity setting)

    var centerX: Double { return x + width / 2 }
    var centerY: Double { return y + height / 2 }
    var grounded: Bool { return mode != .fly }

    private enum Phase { case burst, glide, rest }
    private enum Mode { case fly, landing, perched, hopping, takingOff }

    private let maxTurn = 1.8         // radians per second while bursting
    private let sideOnPitch = 0.6     // side-on pets stay within this of level
    private let fastCursor = 1200.0   // points per second that counts as a swipe

    private var cruise = 0.0, speed = 0.0, turnRate = 0.0, clock = 0.0
    private var targetX = 0.0, targetY = 0.0
    private let wobbleA = Double.random(in: 0..<10), wobbleB = Double.random(in: 0..<10)
    private var burstGlide = false, sideOn = false
    private var phase = Phase.burst
    private var phaseLeft = 0.0

    // Perching. A time of 0 means the pet lacks that animation.
    private var landTime = 0.0, hopTime = 0.0, takeoffTime = 0.0
    private var mode = Mode.fly
    private var modeTime = 0.0, modeLength = 1.0, startX = 0.0, startY = 0.0, endX = 0.0
    private var flyLeft = 0.0, perchLeft = 0.0, restLeft = 0.0, approachLeft = 0.0
    private var approaching = false
    private var perchId = 0
    private var perchTop = 0.0

    // Reactions. A curious swimmer hovers by the cursor and nibbles it.
    private var boostLeft = 0.0, startleCooldown = 0.0, rippleCooldown = 0.0
    private var curiousLeft = 0.0, curiousCooldown = 0.0, nibbleWait = 0.0
    private var curious = false, nibbleIn = false, cursorWasNear = false
    private var bites = 0

    init(world: World, pet: PetSpec) {
        width = Double(pet.width)
        height = Double(pet.height)
        let work = world.workAreas.first ?? CGRect(x: 0, y: 0, width: 800, height: 600)
        x = Double(work.minX) + Double.random(in: 0...1) * max(0, Double(work.width) - width)
        y = Double(work.minY) + Double.random(in: 0...1) * max(0, Double(work.height) - height)
        heading = Double.random(in: 0..<(2 * Double.pi))
        configure(pet: pet)
        pickTarget(world)
    }

    private var canPerch: Bool { return landTime > 0 && takeoffTime > 0 }
    private var size: Double { return max(width, height) }

    func configure(pet: PetSpec) {
        width = Double(pet.width)
        height = Double(pet.height)
        cruise = pet.speed
        burstGlide = pet.has("glide")
        sideOn = pet.headings == 1
        let perches = sideOn && pet.has("land") && pet.has("stand") && pet.has("takeoff")
        landTime = perches ? Wanderer.seconds(pet, "land") : 0
        takeoffTime = perches ? Wanderer.seconds(pet, "takeoff") : 0
        hopTime = perches && pet.has("hop") ? Wanderer.seconds(pet, "hop") : 0

        speed = min(speed, cruise)
        if sideOn { heading = cos(heading) >= 0 ? 0 : Double.pi }
        mode = .fly
        approaching = false
        curious = false
        flyLeft = 6 + Double.random(in: 0..<10)
        startPhase(.burst)
    }

    private static func seconds(_ pet: PetSpec, _ anim: String) -> Double {
        let a = pet.find(anim)
        return Double(a.frames) / a.fps
    }

    // flock is every pet, including this one.
    func step(dt: Double, world: World, flock: [Wanderer]) {
        clock += dt
        modeTime += dt
        oneShot = -1
        animRate = 1
        ripple = false
        boostLeft -= dt
        startleCooldown -= dt
        rippleCooldown -= dt

        react(dt, world)
        switch mode {
        case .fly: fly(dt, world, flock)
        case .landing: land(world)
        case .perched: perch(dt, world)
        case .hopping: hop(world)
        case .takingOff: takeOff(world)
        }
    }

    // The cursor. A swipe nearby always startles. Birds won't let it get
    // close. Swimmers pick a mood when it comes near: sometimes spooked,
    // sometimes hungry, coming over to nibble it once it settles.
    private func react(_ dt: Double, _ world: World) {
        if !world.trackCursor {
            curious = false
            return
        }
        let dx = centerX - world.cursorX, dy = centerY - world.cursorY
        let d = max(0.01, (dx * dx + dy * dy).squareRoot())
        let swipe = world.cursorSpeed > fastCursor && d < size * 3

        if mode == .perched || mode == .hopping {
            if d < size * 1.6 || swipe {
                facingRight = dx > 0   // away from the cursor
                startTakeoff(startled: true)
            }
            return
        }
        if mode != .fly { return }

        curiousCooldown -= dt
        let near = d < size * 2.2
        let entered = near && !cursorWasNear
        cursorWasNear = near

        if swipe && startleCooldown <= 0 {
            startle(dx, dy, boost: 0.9)
            return
        }
        if !burstGlide { return }

        // Spooked this time: the cursor drifting in is enough to send it off.
        if entered && !curious && world.cursorSpeed > 60 && startleCooldown <= 0 && Double.random(in: 0..<1) < 0.4 {
            startle(dx, dy, boost: 0.6)
            return
        }

        if curious {
            curiousLeft -= dt
            if world.cursorSpeed > 300 || curiousLeft <= 0 || d > 900 {
                endCurious(world, cooldown: 4)
                return
            }
            // Dart in until the mouth touches the cursor, or hover just out
            // of reach on the side it came from.
            let r = nibbleIn ? size * 0.36 : size * 0.8
            targetX = world.cursorX + dx / d * r - width / 2
            targetY = world.cursorY + dy / d * r - height / 2
        } else if curiousCooldown <= 0 && world.cursorIdle > 1.0 && d < 700 && d > size * 0.5
                    && Double.random(in: 0..<1) < dt * 0.35 {
            curious = true
            curiousLeft = 14
            bites = 2 + Int.random(in: 0..<3)
            nibbleIn = false
            nibbleWait = 0.4
        }
    }

    private func startle(_ dx: Double, _ dy: Double, boost: Double) {
        startleCooldown = 2
        curious = false
        if approaching {
            approaching = false
            flyLeft = 4 + Double.random(in: 0..<4)
        }
        let away = atan2(dy, dx)
        targetX = x + cos(away) * size * 6
        targetY = y + sin(away) * size * 6
        boostLeft = boost
        if burstGlide {
            heading = Wanderer.wrap(away)   // a sudden dart
            turnRate = 0
            startPhase(.burst)
            makeRipple()
        }
    }

    private func endCurious(_ world: World, cooldown: Double) {
        curious = false
        nibbleIn = false
        curiousCooldown = cooldown
        pickTarget(world)
        startPhase(.glide)
    }

    // Up close to the cursor: hold position facing it, then dart in, bite,
    // and back off, a few times over.
    private func nibble(_ dt: Double, _ world: World) {
        let face = atan2(world.cursorY - centerY, world.cursorX - centerX)
        let turn = Wanderer.wrap(face - heading)
        heading = Wanderer.wrap(heading + max(-4 * dt, min(4 * dt, turn)))
        turnRate = 0

        let rate = nibbleIn ? 9.0 : 2.5   // quick in, slower out
        let nx = x + (targetX - x) * min(1, dt * rate)
        let ny = y + (targetY - y) * min(1, dt * rate)
        speed = ((nx - x) * (nx - x) + (ny - y) * (ny - y)).squareRoot() / max(dt, 0.001)
        x = nx
        y = ny
        keepOnScreens(world)
        let left = ((targetX - x) * (targetX - x) + (targetY - y) * (targetY - y)).squareRoot()

        if nibbleIn {
            animation = "swim"
            animRate = 1.6
            if left < 3 {
                // Bite: a ring of water where the mouth met the cursor.
                ripple = true
                rippleX = world.cursorX
                rippleY = world.cursorY
                rippleCooldown = 0.3
                nibbleIn = false
                nibbleWait = 0.4 + Double.random(in: 0..<0.7)
                bites -= 1
                if bites <= 0 { endCurious(world, cooldown: 15 + Double.random(in: 0..<20)) }
            }
        } else {
            animation = "glide"
            animRate = 0.6
            if left < 6 {
                nibbleWait -= dt
                if nibbleWait <= 0 && abs(turn) < 0.4 { nibbleIn = true }
            }
        }
    }

    private func fly(_ dt: Double, _ world: World, _ flock: [Wanderer]) {
        let work = world.workAt(x: centerX, y: centerY)
        let area = roam(work)

        // Time to come down? Pick somewhere to land and head for it.
        if canPerch && !approaching {
            flyLeft -= dt
            if flyLeft <= 0 { choosePerch(world) }
        }
        if approaching {
            let s = world.find(id: perchId, left: endX, right: endX + width)
            approachLeft -= dt
            if s == nil || abs(s!.top - perchTop) > 2 || approachLeft <= 0 {
                approaching = false   // it moved, vanished, or we couldn't get there
                flyLeft = 4 + Double.random(in: 0..<5)
                pickTarget(world)
            } else {
                targetX = endX
                targetY = perchTop - height
            }
        }

        var dx = targetX - x, dy = targetY - y
        var dist = (dx * dx + dy * dy).squareRoot()

        if curious && dist < size * 0.8 {
            nibble(dt, world)
            return
        }

        if approaching {
            // Close enough that easing to a stop over the landing
            // animation starts at roughly cruising speed.
            if dist <= max(4, speed * landTime * 0.5) {
                startLanding()
                return
            }
        } else if !curious && dist < max(size, cruise * 0.5) {
            // Reached the target: head somewhere new, or settle for a while.
            pickTarget(world)
            if burstGlide && Double.random(in: 0..<1) < 0.3 {
                startPhase(.rest)
                makeRipple()
            }
            dx = targetX - x
            dy = targetY - y
            dist = (dx * dx + dy * dy).squareRoot()
        }

        phaseLeft -= dt
        if burstGlide && phaseLeft <= 0 { startPhase(phase == .burst ? .glide : .burst) }

        // Speed: steady for flyers; thrust and coast for swimmers; a
        // startled pet briefly goes much faster.
        let boosted = boostLeft > 0
        if boosted {
            speed += (cruise * 2.6 - speed) * min(1, dt * 6)
        } else if !burstGlide {
            speed += (cruise - speed) * min(1, dt * 1.5)
        } else if phase == .burst {
            speed += (cruise * 1.6 - speed) * min(1, dt * 3)
        } else {
            speed *= exp(-dt * (phase == .rest ? 1.2 : 0.55))
        }

        // Where it wants to go: toward the target, away from screen edges
        // that have no neighbouring screen (except the ground while coming in
        // to land) and away from other pets, plus a slow wobble so it never
        // moves in a dead straight line.
        var wantX = dx / max(1, dist), wantY = dy / max(1, dist)
        let margin = max(100, size * 2)
        var edge = 0.0
        if !world.onAnyScreen(x: Double(work.minX) - 2, y: centerY) {
            edge += Wanderer.push(x - Double(area.minX), margin, &wantX, 1)
        }
        if !world.onAnyScreen(x: Double(work.maxX) + 2, y: centerY) {
            edge += Wanderer.push(Double(area.maxX) - x, margin, &wantX, -1)
        }
        if !world.onAnyScreen(x: centerX, y: Double(work.minY) - 2) {
            edge += Wanderer.push(y - Double(area.minY), margin, &wantY, 1)
        }
        if !approaching && !world.onAnyScreen(x: centerX, y: Double(work.maxY) + 2) {
            edge += Wanderer.push(Double(area.maxY) - y, margin, &wantY, -1)
        }
        for o in flock where avoidOthers && o !== self && !o.grounded {
            let ox = centerX - o.centerX, oy = centerY - o.centerY
            let od = (ox * ox + oy * oy).squareRoot(), reach = (size + o.size) * 0.55
            if od < reach && od > 0.01 {
                let push = (1 - od / reach) * 1.5
                wantX += ox / od * push
                wantY += oy / od * push
            }
        }
        let wobble = 0.6 * sin(clock * 0.7 + wobbleA) + 0.3 * sin(clock * 1.9 + wobbleB)
        let calm = max(0, 1 - edge) * (approaching || curious ? 0.3 : 1)
        var want = atan2(wantY, wantX) + wobble * calm
        if sideOn { want = level(want) }

        // Turn toward it, limited and eased. Swimmers turn mostly with
        // their tail, so they turn hardest while bursting.
        let limit = maxTurn * (burstGlide && phase != .burst && !boosted ? 0.45 : 1)
        let wantTurn = max(-limit, min(limit, Wanderer.wrap(want - heading) * 2.5))
        turnRate += (wantTurn - turnRate) * min(1, dt * 4)
        heading = Wanderer.wrap(heading + turnRate * dt)
        if burstGlide && phase == .burst && abs(turnRate) > 1.3 { makeRipple() }   // a hard flick of the tail

        x += cos(heading) * speed * dt
        y += sin(heading) * speed * dt
        keepOnScreens(world)

        // Only flip side-on sprites once clearly heading the other way.
        let c = cos(heading)
        if c > 0.15 { facingRight = true } else if c < -0.15 { facingRight = false }

        let pace = min(1, speed / cruise)
        if !burstGlide {
            animation = nil
            animRate = 0.35 + 0.65 * pace
        } else if phase == .burst || boosted {
            animation = "swim"
            animRate = 0.8 + 0.4 * pace
        } else {
            animation = "glide"
            animRate = phase == .rest ? 0.3 : 0.3 + 0.5 * pace
        }
    }

    // The box the pet's top-left corner may move in on one screen.
    private func roam(_ work: CGRect) -> CGRect {
        return CGRect(x: work.minX, y: work.minY,
                      width: max(0, work.width - CGFloat(width)), height: max(0, work.height - CGFloat(height)))
    }

    // Stay on the screen under the pet's centre, but let it cross into a
    // neighbouring screen.
    private func keepOnScreens(_ world: World) {
        let work = world.workAt(x: centerX, y: centerY)
        let area = roam(work)
        let minX = world.onAnyScreen(x: Double(work.minX) - 2, y: centerY) ? Double(work.minX) - width : Double(area.minX)
        let maxX = world.onAnyScreen(x: Double(work.maxX) + 2, y: centerY) ? Double(work.maxX) : Double(area.maxX)
        let minY = world.onAnyScreen(x: centerX, y: Double(work.minY) - 2) ? Double(work.minY) - height : Double(area.minY)
        let maxY = world.onAnyScreen(x: centerX, y: Double(work.maxY) + 2) ? Double(work.maxY) : Double(area.maxY)
        x = max(minX, min(maxX, x))
        y = max(minY, min(maxY, y))
    }

    // Pick a landing spot: the Dock of this screen, or (more often) the
    // visible top edge of a nearby window.
    private func choosePerch(_ world: World) {
        let ground = world.groundAt(x: centerX, y: centerY)
        let windows = world.surfaces.filter { s in
            if s.ground || s.right - s.left < width * 1.5 { return false }
            let mid = (s.left + s.right) / 2
            return abs(mid - centerX) <= 1400 && s.top - height >= Double(world.workAt(x: mid, y: s.top).minY)
        }
        let pick: Surface?
        if !windows.isEmpty && (ground == nil || Double.random(in: 0..<1) < 0.6) {
            pick = windows.randomElement()
        } else {
            pick = ground
        }
        guard let p = pick else {
            flyLeft = 5
            return
        }
        perchId = p.id
        perchTop = p.top
        approaching = true
        approachLeft = 20
        let ahead = (facingRight ? 1.0 : -1.0) * (150 + Double.random(in: 0..<350))
        endX = max(p.left, min(p.right - width, x + ahead))
    }

    private func startLanding() {
        mode = .landing
        modeTime = 0
        modeLength = landTime
        approaching = false
        startX = x
        startY = y
        if abs(endX - startX) > 2 { facingRight = endX > startX }
    }

    // The perch the pet is on, refreshed; nil if it moved, vanished or got
    // covered. The ground just follows the Dock.
    private func currentPerch(_ world: World) -> Surface? {
        var s = world.find(id: perchId, left: x, right: x + width)
        if s == nil && perchId < 0 { s = world.groundAt(x: centerX, y: centerY) }
        guard let found = s, perchId < 0 || abs(found.top - perchTop) <= 1 else { return nil }
        perchId = found.id
        perchTop = found.top
        return found
    }

    // Glide down onto the perch, slowing to a stop as the landing plays.
    private func land(_ world: World) {
        guard let s = world.find(id: perchId, left: endX, right: endX + width), abs(s.top - perchTop) <= 2 else {
            // The window went away under us: pull up and fly on.
            mode = .fly
            heading = facingRight ? -0.5 : Double.pi + 0.5
            speed = cruise * 0.6
            flyLeft = 4 + Double.random(in: 0..<4)
            pickTarget(world)
            return
        }
        perchTop = s.top
        let t = min(1, modeTime / modeLength)
        let ease = 1 - (1 - t) * (1 - t)
        x = startX + (endX - startX) * ease
        y = startY + (perchTop - height - startY) * ease
        animation = "land"
        oneShot = t
        if t >= 1 {
            mode = .perched
            modeTime = 0
            speed = 0
            perchLeft = 12 + Double.random(in: 0..<25)
            restLeft = 1.5 + Double.random(in: 0..<2.5)
        }
    }

    // Stand on the perch; now and then turn around, hop, or take off.
    private func perch(_ dt: Double, _ world: World) {
        guard let s = currentPerch(world) else {
            startTakeoff(startled: true)   // the window moved or was covered
            return
        }
        y = s.top - height
        x = max(s.left, min(s.right - width, x))
        heading = facingRight ? 0 : Double.pi
        animation = "stand"
        perchLeft -= dt
        restLeft -= dt
        if restLeft > 0 { return }

        let nearLeft = x - s.left < width, nearRight = s.right - (x + width) < width
        if perchLeft <= 0 {
            startTakeoff(startled: false)
        } else if (nearLeft && !facingRight) || (nearRight && facingRight) {
            facingRight.toggle()   // turn away from the end
            restLeft = 0.8 + Double.random(in: 0..<1)
        } else if Double.random(in: 0..<1) < 0.25 {
            facingRight.toggle()
            restLeft = 0.8 + Double.random(in: 0..<1.5)
        } else if hopTime > 0 {
            mode = .hopping
            modeTime = 0
            modeLength = hopTime
            startX = x
            endX = max(s.left, min(s.right - width, x + (facingRight ? 1 : -1) * width * 0.35))
        } else {
            restLeft = 2 + Double.random(in: 0..<3)
        }
    }

    // A short hop along the perch in a low arc.
    private func hop(_ world: World) {
        guard let s = currentPerch(world) else {
            startTakeoff(startled: true)
            return
        }
        let t = min(1, modeTime / modeLength)
        x = startX + (endX - startX) * t
        y = s.top - height - size * 0.2 * 4 * t * (1 - t)
        animation = "hop"
        oneShot = t
        if t >= 1 {
            mode = .perched
            modeTime = 0
            restLeft = 0.6 + Double.random(in: 0..<2.4)
        }
    }

    // Startled takeoffs are quicker.
    private func startTakeoff(startled: Bool) {
        mode = .takingOff
        modeTime = 0
        modeLength = takeoffTime / (startled ? 1.7 : 1)
        startX = x
        startY = y
    }

    // Spring up and forward, speeding up, then fly on from there.
    private func takeOff(_ world: World) {
        let t = min(1, modeTime / modeLength)
        let dx = (facingRight ? 1.0 : -1.0) * cruise * modeLength * 0.4
        let lift = size * 0.6
        x = startX + dx * t * t
        y = startY - lift * t * t
        keepOnScreens(world)
        animation = "takeoff"
        oneShot = t
        if t >= 1 {
            // Carry on at the speed and angle it left the perch with.
            let vx = 2 * dx / modeLength, vy = -2 * lift / modeLength
            heading = atan2(vy, vx)
            speed = min(cruise * 1.5, (vx * vx + vy * vy).squareRoot())
            turnRate = 0
            mode = .fly
            modeTime = 0
            flyLeft = 10 + Double.random(in: 0..<15)
            pickTarget(world)
            let work = world.workAt(x: centerX, y: centerY)
            targetY = max(Double(work.minY), y - Double(work.height) * (0.3 + Double.random(in: 0..<0.3)))
        }
    }

    private func makeRipple() {
        if burstGlide && rippleCooldown <= 0 {
            ripple = true
            rippleX = centerX
            rippleY = centerY
            rippleCooldown = 1.2
        }
    }

    private func startPhase(_ next: Phase) {
        phase = next
        switch next {
        case .burst: phaseLeft = 0.5 + Double.random(in: 0..<0.6)
        case .glide: phaseLeft = 1.2 + Double.random(in: 0..<2.3)
        case .rest: phaseLeft = 3 + Double.random(in: 0..<5)
        }
    }

    // Adds a push away from an edge closer than margin; returns its strength.
    private static func push(_ gap: Double, _ margin: Double, _ axis: inout Double, _ away: Double) -> Double {
        if gap >= margin { return 0 }
        let strength = 1 - max(0, gap) / margin
        axis += away * strength * 2
        return strength
    }

    // Keep a heading within sideOnPitch of level, on whichever side it points.
    private func level(_ angle: Double) -> Double {
        let a = Wanderer.wrap(angle)
        if abs(a) <= Double.pi / 2 { return max(-sideOnPitch, min(sideOnPitch, a)) }
        let back = Wanderer.wrap(a - Double.pi)
        return Wanderer.wrap(Double.pi + max(-sideOnPitch, min(sideOnPitch, back)))
    }

    private static func wrap(_ angle: Double) -> Double {
        var a = angle
        while a > Double.pi { a -= 2 * Double.pi }
        while a < -Double.pi { a += 2 * Double.pi }
        return a
    }

    // A new place to head for: usually somewhere on this screen, sometimes
    // on another one. Side-on pets mostly travel sideways.
    private func pickTarget(_ world: World) {
        var work = world.workAt(x: centerX, y: centerY)
        if world.workAreas.count > 1 && Double.random(in: 0..<1) < 0.3, let other = world.workAreas.randomElement() {
            work = other
        }
        let w = Double(work.width), h = Double(work.height)
        for _ in 0..<4 {
            targetX = Double(work.minX) + Double.random(in: 0...1) * max(0, w - width)
            targetY = sideOn
                ? y + Double.random(in: -1...1) * h * 0.3
                : Double(work.minY) + Double.random(in: 0...1) * max(0, h - height)
            targetY = max(Double(work.minY), min(Double(work.maxY) - height, targetY))
            let dx = targetX - x, dy = targetY - y
            if dx * dx + dy * dy > w * w / 16 { break }
        }
    }
}
