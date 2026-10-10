import AppKit
import ApplicationServices

@main struct InsertionChecks {
    static func main() {
        let app = Insertion.AppIdentity(pid: 123, launched: Date(timeIntervalSince1970: 1), bundle: "test.receiver")
        let element = AXUIElementCreateApplication(123)
        let target = Insertion.Target(app: app, element: element)
        let unknown = Insertion.Target(app: app, element: nil)
        let other = Insertion.Target(app: .init(pid: 124, launched: app.launched, bundle: app.bundle), element: element)
        precondition(Insertion.matches(target, unknown))
        precondition(Insertion.matches(unknown, target))
        precondition(!Insertion.matches(target, other))
        precondition(!Insertion.matches(target, .init(app: app, element: AXUIElementCreateApplication(124))))
        precondition(Insertion.adapt("Will Smith", before: "ask ", after: nil) == "Will Smith")
        precondition(Insertion.adapt("Hello", before: nil, after: nil) == "Hello")
        precondition(Insertion.adapt("word", before: "a", after: "next") == " word ")
        precondition(Insertion.adapt("word", before: "(", after: ")") == "word")
        let initial = Insertion.FieldState(value: "A 😀 next", selection: NSRange(location: 5, length: 0))
        let expected = Insertion.replacing(initial, with: "Hello ")!
        var field: Insertion.FieldState? = initial
        var pairs = 0
        func run(_ text: String = "Hello", cancellation: OperationCancellation = .init(),
                 focus: () -> Insertion.Target = { target }, reads: (() -> Insertion.FieldState?)? = nil,
                 sink: (([UniChar], Bool) -> Bool)? = nil) -> Insertion.Result {
            Insertion.perform(text, raw: "Um, Hello", target: target, cancellation: cancellation,
                              currentTarget: focus, readState: reads ?? { field }, context: { nil },
                              post: sink ?? { _, down in if !down { pairs += 1; field = expected }; return true })
        }
        let verified = run()
        precondition(verified.outcome == .verified && pairs == 1)
        precondition(verified.postedAt! <= verified.verifiedAt! && verified.snapshot != nil)
        let snapshot = verified.snapshot!
        precondition(snapshot.range == NSRange(location: 5, length: 6))
        precondition(snapshot.rawReplacement == "Um, Hello ")
        field = initial
        let started = Monotonic.now
        let unchanged = run(sink: { _, _ in true })
        let elapsed = Monotonic.now - started
        precondition(unchanged.outcome == .unchanged && unchanged.snapshot == nil)
        precondition(elapsed >= 0.15 && elapsed < 0.3)
        field = initial
        precondition(run(sink: { _, _ in field = .init(value: "partial", selection: .init(location: 7, length: 0)); return true }).outcome == .unexpected)
        precondition(run(reads: { nil }, sink: { _, _ in true }).outcome == .sent)
        precondition(run(focus: { .init(app: nil, element: nil) }, sink: { _, _ in preconditionFailure("unexpected posting") }).outcome == .unavailable)
        field = initial
        pairs = 0
        precondition(run(focus: { other }).outcome == .targetChanged && pairs == 0)
        let token = OperationCancellation()
        var chunks: [String] = []
        let cancelled = run(String(repeating: "😀", count: 70), cancellation: token, sink: { units, down in
            if down { chunks.append(String(utf16CodeUnits: units, count: units.count)); token.cancel() }
            return true
        })
        precondition(cancelled.outcome == .cancelled && chunks.count == 1 && chunks[0].count == 32)
        precondition(cancelled.postedUnits == 64)
        var focusReads = 0
        chunks = []
        field = initial
        let changed = run(String(repeating: "a", count: 130), focus: {
            focusReads += 1
            return focusReads <= 2 ? target : other
        }, sink: { units, down in if down { chunks.append(String(utf16CodeUnits: units, count: units.count)) }; return true })
        precondition(changed.outcome == .targetChanged && chunks.count == 1)
        field = initial
        var readCount = 0
        let delayed = run(reads: {
            readCount += 1
            return readCount < 4 ? initial : expected
        }, sink: { _, _ in true })
        precondition(delayed.outcome == .verified && delayed.verificationMs >= 40)
        precondition(Insertion.canCorrect(snapshot, current: target, state: snapshot.state))
        precondition(!Insertion.canCorrect(snapshot, current: other, state: snapshot.state))
        precondition(!Insertion.canCorrect(snapshot, current: unknown, state: snapshot.state))
        precondition(!Insertion.canCorrect(snapshot, current: target, state: initial))
        var changedCaret = snapshot.state
        changedCaret = .init(value: changedCaret.value, selection: .init(location: 0, length: 0))
        precondition(!Insertion.canCorrect(snapshot, current: target, state: changedCaret))
        let expired = Insertion.Snapshot(target: target, state: snapshot.state, range: snapshot.range,
                                        rawReplacement: snapshot.rawReplacement, created: Monotonic.now - 301)
        precondition(!Insertion.canCorrect(expired, current: target, state: snapshot.state))
        field = snapshot.state
        var writes = 0
        func correct(_ raw: Bool, writable: Bool = true, writeAccepted: Bool = true) -> Insertion.MutationOutcome {
            Insertion.performCorrection(snapshot, restoreRaw: raw, cancellation: .init(), currentTarget: { target },
                                        readState: { field }, writable: { writable }, select: { range in
                field = .init(value: field!.value, selection: range); return true
            }, write: { text in
                writes += 1
                if writeAccepted { field = Insertion.replacing(field!, with: text) }
                return writeAccepted
            })
        }
        precondition(correct(false) == .verified && field == initial && writes == 1)
        field = snapshot.state
        precondition(correct(true) == .verified && field!.value == "A 😀 Um, Hello next")
        field = snapshot.state
        let beforeWrites = writes
        precondition(correct(false, writable: false) == .noWrite && writes == beforeWrites)
        precondition(correct(false, writeAccepted: false) == .uncertain)
        let selectedInitial = Insertion.FieldState(value: "replace", selection: .init(location: 0, length: 7))
        field = selectedInitial
        let selectedResult = run(reads: { field }, sink: { _, down in
            if !down { field = Insertion.replacing(selectedInitial, with: "Hello") }; return true
        })
        precondition(selectedResult.outcome == .verified && selectedResult.snapshot == nil)
        // All delivery calls use the controlled sink above. This program posts no global events and reads no live field.
        print("Passed controlled delivery, UTF-16 spacing, cancellation, target, and correction checks")
        print("Unchanged-field verification: \(Int(elapsed * 1000)) ms; polling budget: 150 ms")
    }
}
