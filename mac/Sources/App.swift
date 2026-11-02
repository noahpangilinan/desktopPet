import AppKit

// Owns the stage and the menu bar icon, the only way to control the pets,
// since their windows ignore the mouse.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let sizes = [2, 3, 4, 6]
    private let defaults = UserDefaults.standard
    private let agentLabel = "com.noahpangilinan.desktoppet"
    private var petsDir = URL(fileURLWithPath: "/")
    private var statusItem: NSStatusItem?
    private var stage: Stage?
    private var activityLabel: NSTextField?

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let id = Bundle.main.bundleIdentifier,
           NSRunningApplication.runningApplications(withBundleIdentifier: id).count > 1 {
            NSApp.terminate(nil)
            return
        }

        petsDir = AppDelegate.findFolder("pets")
        let names = PetSpec.available(in: petsDir)
        guard !names.isEmpty else { return fail("No pets found in \(petsDir.path)") }

        let stage = Stage(petsDir: petsDir, effectsDir: AppDelegate.findFolder("effects"), scale: savedScale)
        stage.setActivity(defaults.object(forKey: "activity") as? Int ?? Activity.defaultLevel)
        stage.showOverFullscreen = defaults.bool(forKey: "overFullscreen")
        for name in savedPets(names) { try? stage.add(name) }   // a broken folder shouldn't stop the rest
        if stage.pets.isEmpty {
            do { try stage.add(names[0]) } catch { return fail(error.localizedDescription) }
        }
        self.stage = stage

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.toolTip = "Desktop Pet"
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
        updateIcon()

        if !defaults.bool(forKey: "paused") { stage.resume() }
    }

    // Pets to show: the saved list, the older single "pet" setting, or the koi.
    private func savedPets(_ names: [String]) -> [String] {
        var saved = defaults.stringArray(forKey: "pets") ?? []
        if saved.isEmpty, let old = defaults.string(forKey: "pet") { saved = [old] }
        var chosen: [String] = []
        for name in saved where names.contains(name) && !chosen.contains(name) { chosen.append(name) }
        if chosen.isEmpty { chosen = [names.contains("koi") ? "koi" : names[0]] }
        return chosen
    }

    private var savedScale: Int {
        let saved = defaults.integer(forKey: "scale")
        return saved == 0 ? 3 : max(1, min(8, saved))
    }

    // Rebuilt every time the menu opens, so it always shows current state.
    func menuNeedsUpdate(_ menu: NSMenu) {
        guard let stage = stage else { return }
        menu.removeAllItems()

        let pets = NSMenu()
        for name in PetSpec.available(in: petsDir) {
            let showing = stage.pets.contains { $0.spec.name == name }
            pets.addItem(item(name.prefix(1).uppercased() + name.dropFirst(), checked: showing) { [weak self] in
                self?.togglePet(name)
            })
        }
        menu.addItem(submenu("Pets", pets))

        let sizeMenu = NSMenu()
        for size in sizes {
            sizeMenu.addItem(item("\(size)x", checked: size == stage.scale) { [weak self] in self?.setSize(size) })
        }
        menu.addItem(submenu("Size", sizeMenu))

        menu.addItem(.separator())
        menu.addItem(activitySlider(stage))
        menu.addItem(.separator())

        menu.addItem(item("Show over full-screen apps", checked: stage.showOverFullscreen) { [weak self] in
            guard let self = self, let stage = self.stage else { return }
            stage.showOverFullscreen.toggle()
            self.defaults.set(stage.showOverFullscreen, forKey: "overFullscreen")
        })
        menu.addItem(item("Pause", checked: !stage.running) { [weak self] in self?.togglePause() })
        menu.addItem(item("Start at Login", checked: startsAtLogin) { [weak self] in self?.toggleLogin() })
        menu.addItem(.separator())
        menu.addItem(item("Quit", checked: false) { NSApp.terminate(nil) })
    }

    // "Activity: Normal" above a five-step slider, from Calm (lightest on
    // the CPU) to Lively (most aware, smoothest).
    private func activitySlider(_ stage: Stage) -> NSMenuItem {
        let view = NSView(frame: NSRect(x: 0, y: 0, width: 220, height: 52))
        let label = NSTextField(labelWithString: "Activity: \(stage.activity.name)")
        label.frame = NSRect(x: 20, y: 30, width: 190, height: 17)
        label.font = NSFont.menuFont(ofSize: 0)
        let slider = NSSlider(value: Double(stage.levelIndex), minValue: 0,
                              maxValue: Double(Activity.levels.count - 1),
                              target: self, action: #selector(activityChanged(_:)))
        slider.frame = NSRect(x: 18, y: 4, width: 186, height: 24)
        slider.numberOfTickMarks = Activity.levels.count
        slider.allowsTickMarkValuesOnly = true
        slider.isContinuous = true
        view.addSubview(label)
        view.addSubview(slider)
        activityLabel = label
        let item = NSMenuItem()
        item.view = view
        return item
    }

    @objc private func activityChanged(_ sender: NSSlider) {
        guard let stage = stage else { return }
        let level = Int(sender.doubleValue.rounded())
        if level != stage.levelIndex {
            stage.setActivity(level)
            defaults.set(level, forKey: "activity")
        }
        activityLabel?.stringValue = "Activity: \(stage.activity.name)"
    }

    private func togglePet(_ name: String) {
        guard let stage = stage else { return }
        if stage.pets.contains(where: { $0.spec.name == name }) {
            if stage.pets.count > 1 { stage.remove(name) }   // always keep at least one
        } else {
            do { try stage.add(name) } catch { alert(error.localizedDescription) }
        }
        defaults.set(stage.pets.map { $0.spec.name }, forKey: "pets")
        defaults.removeObject(forKey: "pet")
        updateIcon()
    }

    private func setSize(_ size: Int) {
        guard let stage = stage else { return }
        do {
            try stage.setScale(size)
            defaults.set(size, forKey: "scale")
        } catch {
            alert(error.localizedDescription)
        }
        updateIcon()
    }

    private func updateIcon() {
        statusItem?.button?.image = stage?.pets.first?.spec.icon
    }

    private func togglePause() {
        guard let stage = stage else { return }
        if stage.running { stage.pause() } else { stage.resume() }
        defaults.set(!stage.running, forKey: "paused")
    }

    // Start at Login uses a LaunchAgent, which works on every macOS version
    // without extra entitlements.
    private var agentURL: URL {
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(agentLabel).plist")
    }

    private var startsAtLogin: Bool {
        return FileManager.default.fileExists(atPath: agentURL.path)
    }

    private func toggleLogin() {
        let fm = FileManager.default
        if startsAtLogin {
            try? fm.removeItem(at: agentURL)
            return
        }
        let plist: NSDictionary = [
            "Label": agentLabel,
            "ProgramArguments": [Bundle.main.executablePath ?? CommandLine.arguments[0]],
            "RunAtLoad": true,
        ]
        try? fm.createDirectory(at: agentURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        if !plist.write(to: agentURL, atomically: true) {
            alert("Could not write \(agentURL.path)")
        }
    }

    // A folder inside the app bundle, or next to the executable when run loose.
    private static func findFolder(_ name: String) -> URL {
        let fm = FileManager.default
        var candidates: [URL] = []
        if let resources = Bundle.main.resourceURL {
            candidates.append(resources.appendingPathComponent(name))
        }
        let exe = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent()
        candidates.append(exe.appendingPathComponent(name))
        candidates.append(exe.appendingPathComponent("../\(name)").standardized)
        candidates.append(exe.appendingPathComponent("../../\(name)").standardized)
        return candidates.first { fm.fileExists(atPath: $0.path) } ?? candidates[0]
    }

    private func item(_ title: String, checked: Bool, _ handler: @escaping () -> Void) -> NSMenuItem {
        let item = ActionItem(title: title, action: #selector(ActionItem.fire), keyEquivalent: "")
        item.target = item
        item.handler = handler
        item.state = checked ? .on : .off
        return item
    }

    private func submenu(_ title: String, _ menu: NSMenu) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.submenu = menu
        return item
    }

    private func alert(_ message: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Desktop Pet"
        alert.informativeText = message
        alert.runModal()
    }

    private func fail(_ message: String) {
        alert(message)
        NSApp.terminate(nil)
    }
}

// A menu item that runs a closure.
final class ActionItem: NSMenuItem {
    var handler: () -> Void = {}
    @objc func fire() { handler() }
}
