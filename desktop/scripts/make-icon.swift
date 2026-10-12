import AppKit

func image(_ size: Int) -> Data {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                                 samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                 bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    let scale = CGFloat(size) / 1024
    let transform = NSAffineTransform(); transform.scale(by: scale); transform.concat()
    NSColor(calibratedRed: 66 / 255, green: 99 / 255, blue: 235 / 255, alpha: 1).setFill()
    NSBezierPath(roundedRect: NSRect(x: 96, y: 96, width: 832, height: 832), xRadius: 185, yRadius: 185).fill()
    NSColor.white.setFill()
    for (index, height) in [252, 420, 560, 420, 252].enumerated() {
        NSBezierPath(roundedRect: NSRect(x: 304 + index * 92, y: (1024 - height) / 2, width: 48, height: height),
                     xRadius: 24, yRadius: 24).fill()
    }
    NSGraphicsContext.restoreGraphicsState()
    return bitmap.representation(using: .png, properties: [:])!
}
func number(_ value: UInt32) -> Data {
    var value = value.bigEndian
    return withUnsafeBytes(of: &value) { Data($0) }
}
func menuImage(_ scale: Int) -> Data {
    let size = 18 * scale
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                                 samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                 bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    let transform = NSAffineTransform(); transform.scale(by: CGFloat(scale)); transform.concat()
    NSColor.black.setFill()
    for (index, height) in [6, 10, 16, 10, 6].enumerated() {
        NSBezierPath(roundedRect: NSRect(x: index * 4, y: (18 - height) / 2, width: 2, height: height),
                     xRadius: 1, yRadius: 1).fill()
    }
    NSGraphicsContext.restoreGraphicsState()
    return bitmap.representation(using: .png, properties: [:])!
}
let root = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
let png = image(1024)
try png.write(to: root.appendingPathComponent("icon.png"))
var chunks = Data()
for (type, size) in [("ic08", 256), ("ic09", 512), ("ic10", 1024)] {
    let data = image(size)
    chunks.append(Data(type.utf8)); chunks.append(number(UInt32(data.count + 8))); chunks.append(data)
}
var icns = Data("icns".utf8); icns.append(number(UInt32(chunks.count + 8))); icns.append(chunks)
try icns.write(to: root.appendingPathComponent("icon.icns"))
let small = image(256)
var ico = Data([0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 32, 0])
var size = UInt32(small.count).littleEndian
var offset = UInt32(22).littleEndian
withUnsafeBytes(of: &size) { ico.append(contentsOf: $0) }
withUnsafeBytes(of: &offset) { ico.append(contentsOf: $0) }
ico.append(small)
try ico.write(to: root.appendingPathComponent("icon.ico"))
try menuImage(1).write(to: root.appendingPathComponent("trayTemplate.png"))
try menuImage(2).write(to: root.appendingPathComponent("trayTemplate@2x.png"))
try image(32).write(to: root.appendingPathComponent("tray.png"))
