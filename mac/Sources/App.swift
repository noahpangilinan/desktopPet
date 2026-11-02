import AppKit

// Owns the pet and the menu bar icon, the only way to control the pet, since
// its window ignores the mouse.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let sizes = [2, 3, 4, 6]
    private let defaults = UserDefaults.standard
    private let agentLabel = "com.noahpangilinan.desktoppet"
    private var petsDir = URL(fileURLWithPath: "/")
    private var statusItem: NSStatusItem?
    private var controller: PetController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let id = Bundle.main.bundleIdentifier,
           NSRunningApplication.runningApplications(withBundleIdentifier: id).count > 1 {
            NSApp.terminate(nil)
            return
        }

        petsDir = AppDelegate.findPets()
        let names = PetSpec.available(in: petsDir)
        guard !names.isEmpty else { return fail("No pets found in \(petsDir.path)") }
        var name = defaults.string(forKey: "pet") ?? ""
        if !names.contains(name) { name = names.contains("koi") ? "koi" : names[0] }

        let pet: PetSpec
        do { pet = try PetSpec.load(from: petsDir, name: name, scale: scale) } catch {
            return fail(error.localizedDescription)
        }
        let controller = PetController(pet: pet)
        self.controller = controller

        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.image = pet.icon
        item.button?.toolTip = "Desktop Pet"
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item

        controller.show()
        if defaults.bool(forKey: "paused") { controller.pause() }
    }

    private var scale: Int {
        let saved = defaults.integer(forKey: "scale")
        return saved == 0 ? 3 : max(1, min(8, saved))
    }

    // Rebuilt every time the menu opens, so it always shows current state.
    func menuNeedsUpdate(_ menu: NSMenu) {
        guard let controller = controller else { return }
        menu.removeAllItems()

        let pets = NSMenu()
        for name in PetSpec.available(in: petsDir) {
            pets.addItem(item(name.prefix(1).uppercased() + name.dropFirst(),
                              checked: name == controller.pet.name) { [weak self] in
                guard let self = self else { return }
                self.switchPet(name, scale: self.scale)
            })
        }
        menu.addItem(submenu("Pet", pets))

        let sizeMenu = NSMenu()
        for size in sizes {
            sizeMenu.addItem(item("\(size)x", checked: size == scale) { [weak self] in
                guard let self = self, let pet = self.controller?.pet else { return }
                self.switchPet(pet.name, scale: size)
            })
        }
        menu.addItem(submenu("Size", sizeMenu))

        menu.addItem(item("Pause", checked: !controller.running) { [weak self] in self?.togglePause() })
        menu.addItem(item("Start at Login", checked: startsAtLogin) { [weak self] in self?.toggleLogin() })
        menu.addItem(.separator())
        menu.addItem(item("Quit", checked: false) { NSApp.terminate(nil) })
    }

    private func switchPet(_ name: String, scale: Int) {
        guard let controller = controller else { return }
        do {
            let next = try PetSpec.load(from: petsDir, name: name, scale: scale)
            controller.setPet(next)
            statusItem?.button?.image = next.icon
            defaults.set(name, forKey: "pet")
            defaults.set(scale, forKey: "scale")
        } catch {
            alert(error.localizedDescription)
        }
    }

    private func togglePause() {
        guard let controller = controller else { return }
        if controller.running { controller.pause() } else { controller.resume() }
        defaults.set(!controller.running, forKey: "paused")
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

    // pets/ inside the app bundle, or next to the executable when run loose.
    private static func findPets() -> URL {
        let fm = FileManager.default
        var candidates: [URL] = []
        if let resources = Bundle.main.resourceURL {
            candidates.append(resources.appendingPathComponent("pets"))
        }
        let exe = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent()
        candidates.append(exe.appendingPathComponent("pets"))
        candidates.append(exe.appendingPathComponent("../pets").standardized)
        candidates.append(exe.appendingPathComponent("../../pets").standardized)
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
