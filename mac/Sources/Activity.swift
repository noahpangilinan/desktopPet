// How much the pets notice and how often they update: the Activity slider.
// Lower levels tick less often and skip the costlier awareness, so CPU use
// can be traded against liveliness. Same levels as src/Activity.cs.
struct Activity {
    let name: String
    let tick: Double            // seconds between updates
    let windowsEvery: Double    // seconds between window scans; 0 = Dock only
    let cursor: Bool            // react to the mouse
    let ripples: Bool           // water rings under swimmers
    let avoidOthers: Bool       // pets steer around each other

    static let defaultLevel = 2
    static let levels = [
        Activity(name: "Calm", tick: 1.0 / 12, windowsEvery: 0, cursor: false, ripples: false, avoidOthers: false),
        Activity(name: "Relaxed", tick: 1.0 / 20, windowsEvery: 2.0, cursor: false, ripples: true, avoidOthers: true),
        Activity(name: "Normal", tick: 1.0 / 30, windowsEvery: 0.5, cursor: true, ripples: true, avoidOthers: true),
        Activity(name: "Alert", tick: 1.0 / 40, windowsEvery: 0.3, cursor: true, ripples: true, avoidOthers: true),
        Activity(name: "Lively", tick: 1.0 / 60, windowsEvery: 0.15, cursor: true, ripples: true, avoidOthers: true),
    ]
}
