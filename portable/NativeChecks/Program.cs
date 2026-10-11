using Dictate.Core;
using Dictate.Desktop;

if (OperatingSystem.IsMacOS())
{
    if (args is ["--fixture", var macCommand]) { MacChecks.RunFixture(macCommand); return; }
    if (args.Length != 3) throw new ArgumentException("Supply output, audio fixture, and model directories.");
    MacChecks.Run(args);
    return;
}


if (OperatingSystem.IsWindows())
{
    if (args is ["--fixture", var path]) WindowsChecks.RunFixture(path);
    else await WindowsChecks.RunAsync();
    return;
}

var command = args[0];
async Task Signal(string value)
{
    File.WriteAllText(command, value);
    for (var i = 0; i < 100 && File.Exists(command); i++) await Task.Delay(20);
    await Task.Delay(100);
}
var delivery = new LinuxDelivery();
await Signal("first");
Console.WriteLine("Capture first field");
var target = delivery.CaptureTarget();
Console.WriteLine("Insert Unicode text");
var receipt = await target.InsertAsync("Hello ☀.", "hello", CancellationToken.None);
if (receipt.Outcome != DeliveryOutcome.Verified || receipt.Correction is null) throw new Exception("Native insertion failed: " + receipt);
Console.WriteLine("Undo Unicode text");
if (!await receipt.Correction.ReplaceAsync("", CancellationToken.None)) throw new Exception("Native undo failed.");
Console.WriteLine("Capture before focus change");
var stale = delivery.CaptureTarget();
if (stale.Application != "Linux accessible field") throw new Exception("The original field was not captured.");
await Signal("second");
Console.WriteLine("Refuse changed focus");
var refused = await stale.InsertAsync("Wrong field", "wrong", CancellationToken.None);
if (refused.Outcome != DeliveryOutcome.FocusChanged) throw new Exception("A stale field accepted text: " + refused);
await Signal("first");
Console.WriteLine("Capture new dictation");
var editable = delivery.CaptureTarget();
var inserted = await editable.InsertAsync("New text.", "new", CancellationToken.None);
if (inserted.Correction is null) throw new Exception("Missing correction receipt.");
await Signal("edit");
if (await inserted.Correction.ReplaceAsync("", CancellationToken.None)) throw new Exception("Correction changed edited text.");
Console.WriteLine("Native GTK insertion, Unicode readback, undo, same-window focus change, and refusal after editing passed.");
