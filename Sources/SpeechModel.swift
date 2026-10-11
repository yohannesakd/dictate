// All speech engines run offline on this Mac. Phonon-2 is the default.

import Foundation

enum SpeechModel: String, CaseIterable, Identifiable {
    case phonon, whistle, whisper

    static let `default` = SpeechModel.phonon
    static let preferenceKey = "speechModel"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .phonon: return "Phonon-2"
        case .whistle: return "Whistle"
        case .whisper: return "Whisper (base)"
        }
    }

    /// One sentence for the settings footer.
    var summary: String {
        switch self {
        case .whisper: return "Runs the multilingual base model on the CPU."
        case .phonon: return "Runs on the Neural Engine and stays loaded for fast results."
        case .whistle: return "Runs on the CPU. Supports English, German, French, Spanish, Italian, Dutch, and Polish."
        }
    }

    /// Credit and licence, shown in settings and in the About footer.
    var credit: String {
        switch self {
        case .whisper: return "Whisper by OpenAI, run with whisper.cpp. Licence: MIT."
        case .phonon: return "Phonon-2 by Fermion Research, based on NVIDIA Parakeet TDT 0.6B v3. Model licence: CC-BY-4.0."
        case .whistle: return "Whistle by Cactus Compute, run with the Needle engine. Licence: Apache-2.0."
        }
    }

    /// Phonon-2 snapshots during recording keep the Neural Engine warm. Whistle has nothing to warm,
    /// and each snapshot would start a process.
    var warmsWithSnapshots: Bool { self == .phonon }
}
