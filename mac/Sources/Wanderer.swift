import Foundation

// Moves a pet around a rectangle the way an animal would: it keeps a heading
// and can only turn so fast, so paths curve; a slow wobble makes them meander;
// edges push it back toward open space. Pets with a glide animation swim in
// bursts (a few strong tail beats) followed by coasting, and sometimes rest.
//
// Same behaviour as src/Wanderer.cs. Coordinates are y-down (like the sprite
// headings), so the caller flips them for the screen.
final class Wanderer {
    var x = 0.0, y = 0.0              // top-left of the pet
    var heading = 0.0                 // radians, 0 = right, clockwise
    var facingRight = true            // for side-on pets
    var animation: String?            // nil = the pet's first animation
    var animRate = 1.0                // multiplier on the animation's fps

    private enum Phase { case burst, glide, rest }

    private let maxTurn = 1.8         // radians per second while bursting
    private let sideOnPitch = 0.6     // side-on pets stay within this of level

    private var cruise = 0.0, speed = 0.0, turnRate = 0.0, clock = 0.0
    private var targetX = 0.0, targetY = 0.0
    private let wobbleA = Double.random(in: 0..<10), wobbleB = Double.random(in: 0..<10)
    private var burstGlide = false, sideOn = false
    private var phase = Phase.burst
    private var phaseLeft = 0.0

    init(area: CGRect, cruise: Double, burstGlide: Bool, sideOn: Bool) {
        x = Double(area.minX) + Double.random(in: 0...1) * Double(area.width)
        y = Double(area.minY) + Double.random(in: 0...1) * Double(area.height)
        heading = Double.random(in: 0..<(2 * Double.pi))
        configure(cruise: cruise, burstGlide: burstGlide, sideOn: sideOn)
        pickTarget(area)
    }

    func configure(cruise: Double, burstGlide: Bool, sideOn: Bool) {
        self.cruise = cruise
        self.burstGlide = burstGlide
        self.sideOn = sideOn
        speed = min(speed, cruise)
        if sideOn { heading = cos(heading) >= 0 ? 0 : Double.pi }
        startPhase(.burst)
    }

    // area is where the pet's top-left corner may go; size is the pet's size.
    func step(dt: Double, area: CGRect, size: Double) {
        let left = Double(area.minX), right = Double(area.maxX)
        let top = Double(area.minY), bottom = Double(area.maxY)
        clock += dt
        var dx = targetX - x, dy = targetY - y
        var dist = (dx * dx + dy * dy).squareRoot()

        // Reached the target: head somewhere new, or settle down for a while.
        if dist < max(size, cruise * 0.5) {
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

        // Where it wants to go: toward the target, away from nearby edges,
        // plus a slow wobble so it never moves in a dead straight line.
        var wantX = dx / max(1, dist), wantY = dy / max(1, dist)
        let margin = max(100, size * 2)
        var edge = 0.0
        edge += Wanderer.push(x - left, margin, &wantX, 1)
        edge += Wanderer.push(right - x, margin, &wantX, -1)
        edge += Wanderer.push(y - top, margin, &wantY, 1)
        edge += Wanderer.push(bottom - y, margin, &wantY, -1)
        let wobble = 0.6 * sin(clock * 0.7 + wobbleA) + 0.3 * sin(clock * 1.9 + wobbleB)
        var want = atan2(wantY, wantX) + wobble * max(0, 1 - edge)
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
