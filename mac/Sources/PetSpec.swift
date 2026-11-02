import AppKit
import ImageIO

// One named animation: a block of rows in the sheet starting at row. A side-on
// pet uses one row (mirrored for facing left); a pet with N headings uses N rows.
struct Anim {
    let name: String
    let row: Int
    let frames: Int
    let fps: Double
}

struct PetError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}

// One pet, loaded from pets/<name>/pet.ini and sheet.png. Frames are cropped
// from the sheet on demand (cheap: crops share the sheet's pixels) and scaled
// up on screen with nearest-neighbour filtering.
final class PetSpec {
    let name: String
    let width: CGFloat, height: CGFloat   // scaled frame size in points
    let headings: Int                     // 1 = side-on, mirrored to face left
    let speed: Double                     // points per second, already scaled
    let anims: [Anim]
    let icon: NSImage

    private let sheet: CGImage
    private let frameWidth: Int, frameHeight: Int

    private init(name: String, sheet: CGImage, frameWidth: Int, frameHeight: Int,
                 headings: Int, anims: [Anim], speed: Double, scale: Int) {
        self.name = name
        self.sheet = sheet
        self.frameWidth = frameWidth
        self.frameHeight = frameHeight
        self.headings = headings
        self.anims = anims
        self.speed = speed * Double(scale)
        width = CGFloat(frameWidth * scale)
        height = CGFloat(frameHeight * scale)

        let first = sheet.cropping(to: CGRect(x: 0, y: anims[0].row * frameHeight,
                                              width: frameWidth, height: frameHeight))
        let side = CGFloat(max(frameWidth, frameHeight))
        let w = 18 * CGFloat(frameWidth) / side, h = 18 * CGFloat(frameHeight) / side
        icon = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            guard let first = first else { return false }
            NSGraphicsContext.current?.imageInterpolation = .none
            NSImage(cgImage: first, size: .zero)
                .draw(in: NSRect(x: (18 - w) / 2, y: (18 - h) / 2, width: w, height: h))
            return true
        }
    }

    func has(_ anim: String) -> Bool {
        return anims.contains { $0.name == anim }
    }

    // The named animation, or the first one if this pet does not have it.
    func find(_ anim: String?) -> Anim {
        return anims.first { $0.name == anim } ?? anims[0]
    }

    // Which way to draw a pet heading along angle (radians, 0 = right,
    // clockwise on screen). Side-on pets use 0 = right, 1 = left.
    func direction(heading: Double, facingRight: Bool) -> Int {
        if headings == 1 { return facingRight ? 0 : 1 }
        let h = Int((heading / (2 * Double.pi / Double(headings))).rounded())
        return ((h % headings) + headings) % headings
    }

    // The frame's image. Side-on pets facing left are mirrored by the caller.
    func image(_ anim: Anim, frame: Int, direction: Int) -> CGImage? {
        let row = anim.row + (headings > 1 ? direction : 0)
        return sheet.cropping(to: CGRect(x: frame * frameWidth, y: row * frameHeight,
                                         width: frameWidth, height: frameHeight))
    }

    // Folder names under petsDir that look like pets, sorted.
    static func available(in petsDir: URL) -> [String] {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: petsDir.path) else { return [] }
        return names.filter { name in
            let dir = petsDir.appendingPathComponent(name)
            return fm.fileExists(atPath: dir.appendingPathComponent("pet.ini").path)
                && fm.fileExists(atPath: dir.appendingPathComponent("sheet.png").path)
        }.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    static func load(from petsDir: URL, name: String, scale: Int) throws -> PetSpec {
        let dir = petsDir.appendingPathComponent(name)
        let (ini, sections) = Ini.read(dir.appendingPathComponent("pet.ini"))
        let frameWidth = Ini.int(ini, "frameWidth", 32)
        let frameHeight = Ini.int(ini, "frameHeight", 32)
        let headings = max(1, Ini.int(ini, "headings", 1))
        let speed = Ini.double(ini, "speed", 50)

        // Each [section] is an animation. A file without sections is one
        // animation described by top-level frames/fps, starting at row 0.
        var anims = sections.map { section -> Anim in
            let p = section + "."
            return Anim(name: section, row: Ini.int(ini, p + "row", 0),
                        frames: Ini.int(ini, p + "frames", 1), fps: Ini.double(ini, p + "fps", 8))
        }
        if anims.isEmpty {
            anims = [Anim(name: "default", row: 0, frames: Ini.int(ini, "frames", 1),
                          fps: Ini.double(ini, "fps", 8))]
        }

        let url = dir.appendingPathComponent("sheet.png") as CFURL
        guard let source = CGImageSourceCreateWithURL(url, nil),
              let sheet = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
            throw PetError("\(name): could not read sheet.png")
        }
        guard frameWidth > 0, frameHeight > 0 else {
            throw PetError("\(name): pet.ini frame size must be positive")
        }
        for a in anims where a.frames <= 0 || a.row < 0 || a.fps <= 0
            || a.frames * frameWidth > sheet.width || (a.row + headings) * frameHeight > sheet.height {
            throw PetError("\(name): animation '\(a.name)' does not fit sheet.png")
        }
        return PetSpec(name: name, sheet: sheet, frameWidth: frameWidth, frameHeight: frameHeight,
                       headings: headings, anims: anims, speed: speed, scale: scale)
    }
}

// Minimal ini reader; ';' and '#' start comments. Keys inside a [section] are
// stored as "section.key". Keys are case-insensitive.
enum Ini {
    static func read(_ url: URL) -> ([String: String], [String]) {
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return ([:], []) }
        var values: [String: String] = [:]
        var sections: [String] = []
        var prefix = ""
        for raw in text.components(separatedBy: .newlines) {
            var line = raw
            if let comment = line.firstIndex(where: { $0 == ";" || $0 == "#" }) {
                line = String(line[..<comment])
            }
            line = line.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("[") && line.hasSuffix("]") {
                let section = String(line.dropFirst().dropLast()).trimmingCharacters(in: .whitespaces)
                sections.append(section)
                prefix = section.lowercased() + "."
                continue
            }
            if let eq = line.firstIndex(of: "="), eq != line.startIndex {
                let key = line[..<eq].trimmingCharacters(in: .whitespaces).lowercased()
                values[prefix + key] = line[line.index(after: eq)...].trimmingCharacters(in: .whitespaces)
            }
        }
        return (values, sections)
    }

    static func int(_ ini: [String: String], _ key: String, _ fallback: Int) -> Int {
        return ini[key.lowercased()].flatMap { Int($0) } ?? fallback
    }

    static func double(_ ini: [String: String], _ key: String, _ fallback: Double) -> Double {
        return ini[key.lowercased()].flatMap { Double($0) } ?? fallback
    }
}
