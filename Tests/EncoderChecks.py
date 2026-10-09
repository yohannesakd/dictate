#!/usr/bin/env python3
"""Compile the patched capacity selector with controlled model shapes."""
from pathlib import Path
import subprocess
repo = Path(__file__).resolve().parents[1]
root = Path('/tmp/dictate-sol-fixes/encoder-checks')
root.mkdir(exist_ok=True)
lines = (repo / 'patches/phonon-frame-capacity.patch').read_text().splitlines()
patched = '\n'.join(line[1:] for line in lines if line.startswith(' ') or (line.startswith('+') and not line.startswith('+++')))
method = patched.split('    func model(forFrames', 1)[1].split('    /// Preload', 1)[0]
(root / 'Checks.swift').write_text(r'''
import Foundation
final class Constraint { let shape: [NSNumber]; init(_ n: Int) { shape = [1, 128, NSNumber(value: n)] } }
final class Description { let multiArrayConstraint: Constraint?; init(_ n: Int) { multiArrayConstraint = Constraint(n) } }
final class ModelDescription {
    let inputDescriptionsByName: [String: Description]
    init(_ n: Int) { inputDescriptionsByName = ["mel": Description(n)] }
}
final class MLModel {
    let modelDescription: ModelDescription
    init(_ n: Int) { modelDescription = ModelDescription(n) }
}
final class Selector {
    let functionSeconds: [Double] = [5, 10, 15, 35]
    let modelLock = NSLock()
    var models: [Int: MLModel] = [:]
    var loading: Set<Int> = []
    var loads: [Int] = []
    static func framesFor(seconds: Double) -> Int { 1 + Int((seconds * 16000).rounded()) / 160 }
    func loadFunction(_ key: Int) throws -> MLModel {
        loads.append(key)
        let model = MLModel(Self.framesFor(seconds: Double(key)))
        models[key] = model
        return model
    }
    func model(forFrames''' + method + r'''
}
@main struct Checks {
    static func main() throws {
        for (frames, capacity) in [(500, 501), (501, 501), (502, 1001), (1001, 1001), (1002, 1501),
                                   (1499, 1501), (1500, 1501), (1501, 1501), (1502, 3501), (3501, 3501)] {
            let selector = Selector()
            let (_, actual) = try selector.model(forFrames: frames)
            precondition(actual == capacity && selector.loads.count == 1)
        }
        do { _ = try Selector().model(forFrames: 3502); preconditionFailure("expected capacity rejection") } catch {}
        let invalid = Selector()
        invalid.models[15] = MLModel(1499)
        do { _ = try invalid.model(forFrames: 1500); preconditionFailure("expected declared-shape rejection") } catch {}
        let loading = Selector()
        loading.loading = [15]
        loading.models[35] = MLModel(3501)
        let fallback = try loading.model(forFrames: 1500).1
        precondition(fallback == 3501)
        precondition(loading.loads.isEmpty)
        print("Passed encoder frame boundaries, actual-shape rejection, and ready-function fallback checks")
    }
}
''')
subprocess.run(['xcrun', 'swiftc', '-swift-version', '5', '-parse-as-library', str(root / 'Checks.swift'), '-o', str(root / 'checks')], check=True)
subprocess.run([str(root / 'checks')], check=True)
