import Foundation

// Moves a pet around a rectangle the way an animal would: it keeps a heading
// and can only turn so fast, so paths curve; a slow wobble makes them meander;
// edges push it back toward open space. Pets with a glide animation swim in
// bursts (a few strong tail beats) followed by coasting, and sometimes rest.
//
// Side-on pets with land, stand and takeoff animations (birds) also perch:
// every so often they fly down to the bottom edge (the top of the Dock),
// land, stand around hopping and turning, then take off again.
//
// Same behaviour as src/Wanderer.cs. Coordinates are y-down (like the sprite
// headings), so the caller flips them for the screen.
final class Wanderer {
    var x = 0.0, y = 0.0              // top-left of the pet
    var heading = 0.0                 // radians, 0 = right, clockwise
    var facingRight = true            // for side-on pets
    var animation: String?            // nil = the pet's first animation
    var animRate = 1.0                // multiplier on the animation's fps
    var oneShot = -1.0                // 0...1 through a play-once animation, or -1 to loop

    private enum Phase { case burst, glide, rest }
    private enum Mode { case fly, landing, perched, hopping, takingOff }

    private let maxTurn = 1.8         // radians per second while bursting
    private let sideOnPitch = 0.6     // side-on pets stay within this of level

    private var cruise = 0.0, speed = 0.0, turnRate = 0.0, clock = 0.0
    private var targetX = 0.0, targetY = 0.0
    private let wobbleA = Double.random(in: 0..<10), wobbleB = Double.random(in: 0..<10)
    private var burstGlide = false, sideOn = false
    private var phase = Phase.burst
    private var phaseLeft = 0.0

    // Perching. A time of 0 means the pet lacks that animation.
    private var landTime = 0.0, hopTime = 0.0, takeoffTime = 0.0
    private var mode = Mode.fly
    private var modeTime = 0.0, startX = 0.0, startY = 0.0, endX = 0.0
    private var flyLeft = 0.0, perchLeft = 0.0, restLeft = 0.0, approachLeft = 0.0
    private var approaching = false

    init(area: CGRect, pet: PetSpec) {
        x = Double(area.minX) + Double.random(in: 0...1) * Double(area.width)
        y = Double(area.minY) + Double.random(in: 0...1) * Double(area.height)
        heading = Double.random(in: 0..<(2 * Double.pi))
        configure(pet: pet)
        pickTarget(area)
    }

    private var canPerch: Bool { return landTime > 0 && takeoffTime > 0 }

    func configure(pet: PetSpec) {
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
        flyLeft = 6 + Double.random(in: 0..<10)
        startPhase(.burst)
    }

    private static func seconds(_ pet: PetSpec, _ anim: String) -> Double {
        let a = pet.find(anim)
        return Double(a.frames) / a.fps
    }

    // area is where the pet's top-left corner may go; size is the pet's size.
    // Its bottom edge is the ground birds land on.
    func step(dt: Double, area: CGRect, size: Double) {
        clock += dt
        modeTime += dt
        oneShot = -1
        animRate = 1
        switch mode {
        case .fly: fly(dt, area, size)
        case .landing: land(area)
        case .perched: perch(dt, area, size)
        case .hopping: hop(area, size)
        case .takingOff: takeOff(area, size)
        }
    }

    private func fly(_ dt: Double, _ area: CGRect, _ size: Double) {
        let left = Double(area.minX), right = Double(area.maxX)
        let top = Double(area.minY), bottom = Double(area.maxY)

        // Time to come down? Pick a spot on the ground ahead and head for it.
        if canPerch && !approaching {
            flyLeft -= dt
            if flyLeft <= 0 {
                approaching = true
                approachLeft = 20
                let ahead = (facingRight ? 1.0 : -1.0) * Double(area.width) * (0.15 + Double.random(in: 0..<0.3))
                targetX = max(left + size, min(right - size, x + ahead))
            }
        }
        if approaching {
            targetY = bottom
            approachLeft -= dt
            if approachLeft <= 0 {
                approaching = false   // couldn't get there; try again later
                flyLeft = 5 + Double.random(in: 0..<5)
                pickTarget(area)
            }
        }

        var dx = targetX - x, dy = targetY - y
        var dist = (dx * dx + dy * dy).squareRoot()

        if approaching {
            // Close enough that easing to a stop over the landing
            // animation starts at roughly cruising speed.
            if dist <= max(4, speed * landTime * 0.5) {
                startLanding()
                return
            }
        } else if dist < max(size, cruise * 0.5) {
            // Reached the target: head somewhere new, or settle down for a while.
            pickTarget(area)
            if burstGlide && Double.random(in: 0..<1) < 0.3 { startPhase(.rest) }
            dx = targetX - x
            dy = targetY - y
            dist = (dx * dx + dy * dy).squareRoot()
        }

        phaseLeft -= dt
        if burstGlide && phaseLeft <= 0 { startPhase(phase == .burst ? .glide : .burst) }

        // Speed: steady for flyers; thrust and coast for swimmers.
        if !burstGlide {
            speed += (cruise - speed) * min(1, dt * 1.5)
        } else if phase == .burst {
            speed += (cruise * 1.6 - speed) * min(1, dt * 3)
        } else {
            speed *= exp(-dt * (phase == .rest ? 1.2 : 0.55))
        }

        // Where it wants to go: toward the target, away from nearby edges
        // (except the ground while coming in to land), plus a slow wobble so
        // it never moves in a dead straight line.
        var wantX = dx / max(1, dist), wantY = dy / max(1, dist)
        let margin = max(100, size * 2)
        var edge = 0.0
        edge += Wanderer.push(x - left, margin, &wantX, 1)
        edge += Wanderer.push(right - x, margin, &wantX, -1)
        edge += Wanderer.push(y - top, margin, &wantY, 1)
        if !approaching { edge += Wanderer.push(bottom - y, margin, &wantY, -1) }
        let wobble = 0.6 * sin(clock * 0.7 + wobbleA) + 0.3 * sin(clock * 1.9 + wobbleB)
        var want = atan2(wantY, wantX) + wobble * max(0, 1 - edge) * (approaching ? 0.3 : 1)
        if sideOn { want = level(want) }

        // Turn toward it, limited and eased. Swimmers turn mostly with
        // their tail, so they turn hardest while bursting.
        let limit = maxTurn * (burstGlide && phase != .burst ? 0.45 : 1)
        let wantTurn = max(-limit, min(limit, Wanderer.wrap(want - heading) * 2.5))
        turnRate += (wantTurn - turnRate) * min(1, dt * 4)
        heading = Wanderer.wrap(heading + turnRate * dt)

        x += cos(heading) * speed * dt
        y += sin(heading) * speed * dt
        x = max(left, min(right, x))
        y = max(top, min(bottom, y))

        // Only flip side-on sprites once clearly heading the other way.
        let c = cos(heading)
        if c > 0.15 { facingRight = true } else if c < -0.15 { facingRight = false }

        let pace = min(1, speed / cruise)
        if !burstGlide {
            animation = nil
            animRate = 0.35 + 0.65 * pace
        } else if phase == .burst {
            animation = "swim"
            animRate = 0.8 + 0.4 * pace
        } else {
            animation = "glide"
            animRate = phase == .rest ? 0.3 : 0.3 + 0.5 * pace
        }
    }

    private func startLanding() {
        mode = .landing
        modeTime = 0
        approaching = false
        startX = x
        startY = y
        endX = targetX
        if abs(endX - startX) > 2 { facingRight = endX > startX }
    }

    // Glide down onto the ground, slowing to a stop as the landing plays.
    private func land(_ area: CGRect) {
        let t = min(1, modeTime / landTime)
        let ease = 1 - (1 - t) * (1 - t)
        x = startX + (endX - startX) * ease
        y = startY + (Double(area.maxY) - startY) * ease
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

    // Stand on the ground; now and then turn around, hop, or take off.
    private func perch(_ dt: Double, _ area: CGRect, _ size: Double) {
        let left = Double(area.minX), right = Double(area.maxX)
        y = Double(area.maxY)
        x = max(left, min(right, x))
        heading = facingRight ? 0 : Double.pi
        animation = "stand"
        perchLeft -= dt
        restLeft -= dt
        if restLeft > 0 { return }

        let nearLeft = x - left < size, nearRight = right - x < size
        if perchLeft <= 0 {
            startTakeoff()
        } else if (nearLeft && !facingRight) || (nearRight && facingRight) {
            facingRight.toggle()   // turn away from the edge
            restLeft = 0.8 + Double.random(in: 0..<1)
        } else if Double.random(in: 0..<1) < 0.25 {
            facingRight.toggle()
            restLeft = 0.8 + Double.random(in: 0..<1.5)
        } else if hopTime > 0 {
            mode = .hopping
            modeTime = 0
            startX = x
            endX = max(left, min(right, x + (facingRight ? 1 : -1) * size * 0.35))
        } else {
            restLeft = 2 + Double.random(in: 0..<3)
        }
    }

    // A short hop along the ground in a low arc.
    private func hop(_ area: CGRect, _ size: Double) {
        let t = min(1, modeTime / hopTime)
        x = startX + (endX - startX) * t
        y = Double(area.maxY) - size * 0.2 * 4 * t * (1 - t)
        animation = "hop"
        oneShot = t
        if t >= 1 {
            mode = .perched
            modeTime = 0
            restLeft = 0.6 + Double.random(in: 0..<2.4)
        }
    }

    private func startTakeoff() {
        mode = .takingOff
        modeTime = 0
        startX = x
        startY = y
    }

    // Spring up and forward, speeding up, then fly on from there.
    private func takeOff(_ area: CGRect, _ size: Double) {
        let t = min(1, modeTime / takeoffTime)
        let dx = (facingRight ? 1.0 : -1.0) * cruise * takeoffTime * 0.4
        let lift = size * 0.6
        x = max(Double(area.minX), min(Double(area.maxX), startX + dx * t * t))
        y = max(Double(area.minY), startY - lift * t * t)
        animation = "takeoff"
        oneShot = t
        if t >= 1 {
            // Carry on at the speed and angle it left the ground with.
            let vx = 2 * dx / takeoffTime, vy = -2 * lift / takeoffTime
            heading = atan2(vy, vx)
            speed = min(cruise, (vx * vx + vy * vy).squareRoot())
            turnRate = 0
            mode = .fly
            modeTime = 0
            flyLeft = 10 + Double.random(in: 0..<15)
            targetX = Double(area.minX) + Double.random(in: 0...1) * Double(area.width)
            targetY = max(Double(area.minY), y - Double(area.height) * (0.3 + Double.random(in: 0..<0.3)))
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

    private func pickTarget(_ area: CGRect) {
        // A few tries for a target that is a decent distance away. Side-on
        // pets mostly travel sideways.
        let w = Double(area.width), h = Double(area.height)
        for _ in 0..<4 {
            targetX = Double(area.minX) + Double.random(in: 0...1) * w
            targetY = sideOn
                ? y + Double.random(in: -1...1) * h * 0.3
                : Double(area.minY) + Double.random(in: 0...1) * h
            targetY = max(Double(area.minY), min(Double(area.maxY), targetY))
            let dx = targetX - x, dy = targetY - y
            if dx * dx + dy * dy > w * w / 16 { break }
        }
    }
}
