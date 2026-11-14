using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ServoAnimator;

internal sealed class EditorApiRequest
{
    public string Method { get; set; }
    public string RequestId { get; set; }
    public string ExpectedToken { get; set; }
    public string Description { get; set; }
    public string LibraryKind { get; set; }
    public string Name { get; set; }
    public bool? Enabled { get; set; }
    public double? Seconds { get; set; }
    public int? Index { get; set; }
    public int? ToIndex { get; set; }
    public string SequencePath { get; set; }
    public bool? Loop { get; set; }
    public bool? DriveUrdf { get; set; }
    public bool? DrivePhysical { get; set; }
    public bool? UseEditorOutputs { get; set; }
    public string StreamDeckSession { get; set; }
    public string BackgroundImageSource { get; set; }
    public BackdropCommand Backdrop { get; set; }
    public UrdfFraming Framing { get; set; }
    public double? ForwardMeters { get; set; }
    public double? TurnDegrees { get; set; }
    public bool? Reverse { get; set; }
    public double? Speed { get; set; }
    public double? Fill { get; set; }
    public string Screen { get; set; }
    public string MediaPath { get; set; }
    public string Fit { get; set; }
    public double? StartSeconds { get; set; }
    public double? Volume { get; set; }
    public string Direction { get; set; }
    public bool? Particles { get; set; }
    public string Sound { get; set; }
    public bool? Rgb { get; set; }
    public string Text { get; set; }
    public string Image { get; set; }
    public string Color { get; set; }
    public bool? Hide { get; set; }
    public bool? Swap { get; set; }
    public int? Shots { get; set; }
    public double? Interval { get; set; }
    public double? Range { get; set; }
    public double? Length { get; set; }
    /// <summary>get_video_frames: timeline times, or startSeconds/endSeconds with interval.</summary>
    public List<double> Times { get; set; }
    public double? EndSeconds { get; set; }
    /// <summary>Largest image width in pixels for the image-returning reads.</summary>
    public int? MaxWidth { get; set; }
    public List<EditorApiCommand> Commands { get; set; } = new();
    public List<string> EnableSplines { get; set; } = new();
}

internal sealed class EditorApiCommand
{
    public string Servo { get; set; }
    public string Control { get; set; }
    public double AfterSeconds { get; set; }
    public int? Value { get; set; }
    public string Speed { get; set; } = "N/C";
    public string Reason { get; set; } = "";
}

internal static class EditorApiCommands
{
    public static List<ServoCommand> Prepare(EditorApiRequest request, double cursor,
        IEnumerable<ServoCommand> existing)
    {
        if (request.Commands == null || request.Commands.Count is < 1 or > 2000)
            throw new InvalidOperationException("Supply 1 to 2000 commands.");
        var result = new List<ServoCommand>();
        foreach (var input in request.Commands)
        {
            if (input == null || !Enum.TryParse<ServoNames>(input.Servo, false, out var servo) ||
                !Enum.IsDefined(servo) || ServoCommand.IsTextValued(servo))
                throw new InvalidOperationException("Use a numeric servo name from get_state.controls.");
            var range = ServoCommand.RangeFor(servo);
            if (input.Value == null || input.Value < range.Min || input.Value > range.Max)
                throw new InvalidOperationException($"{servo} requires a value from {range.Min} to {range.Max}.");
            if (!double.IsFinite(input.AfterSeconds) || input.AfterSeconds < 0 ||
                !double.IsFinite(cursor + input.AfterSeconds) || cursor + input.AfterSeconds > 86400)
                throw new InvalidOperationException("Command times must be between the captured cursor and 86400 seconds.");
            if (!ServoCommand.TryParseSpeed(input.Speed, out var speed) || !Enum.IsDefined(speed))
                throw new InvalidOperationException("Invalid speed; use N/C, Default, Slow, Fast, or Crawl.");
            RobotControls? control = null;
            if (input.Control != null)
            {
                if (!Enum.TryParse<RobotControls>(input.Control, false, out var child) ||
                    !ServoConfiguration.ControlsFor(servo).Contains(child))
                    throw new InvalidOperationException($"Invalid child control for {servo}.");
                control = child;
            }
            result.Add(new ServoCommand { Servo = servo, Control = control,
                OffsetSeconds = ServoCommand.TimeKey(cursor + input.AfterSeconds),
                NumericValue = input.Value.Value, Speed = speed, Reason = input.Reason ?? "" });
        }
        if (CommandConflicts.Find(existing.Concat(result)).Count > 0)
            throw new InvalidOperationException("Command conflict: a target already has a command at that millisecond. Nothing was changed.");
        return result;
    }
}

/// <summary>One line of UTF-8 JSON per connection. Only this Windows user can connect.</summary>
internal sealed class EditorApiServer : IDisposable
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static string PipeName(int processId) => $"Johnny5.AnimationEditor.{processId}";
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<EditorApiRequest, Task<object>> _handler;
    public Task Completion { get; }

    public EditorApiServer(string name, Func<EditorApiRequest, Task<object>> handler)
    {
        _handler = handler;
        Completion = Task.Run(() => Run(name));
    }

    private async Task Run(string name)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                object response;
                try
                {
                    var text = new StringBuilder();
                    var buffer = new char[1];
                    while (await reader.ReadAsync(buffer.AsMemory(), timeout.Token) != 0 && buffer[0] != '\n')
                    {
                        if (text.Length >= 1024 * 1024) throw new InvalidOperationException("Request exceeds 1 MiB.");
                        text.Append(buffer[0]);
                    }
                    var request = JsonSerializer.Deserialize<EditorApiRequest>(text.ToString(), Json)
                        ?? throw new InvalidOperationException("Missing request.");
                    // Dispatch once. A disconnected client can safely retry with the same requestId.
                    response = await _handler(request);
                }
                catch (Exception ex) { response = new { ok = false, error = ex.Message }; }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json).AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    public void Dispose() => _stop.Cancel();
}
