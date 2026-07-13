using System.Globalization;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameCsvFormatting
{
    public const string Header = "CaptureSessionId,Timestamp,ProcessId,ProcessName,SwapChainAddress,FPS,FrameTimeMs,CpuBusyMs,CpuWaitMs,GpuLatencyMs,GpuTimeMs,GpuBusyMs,GpuWaitMs,RenderLatencyMs,DisplayLatencyMs,DisplayedTimeMs,ClickToPhotonLatencyMs,Runtime,PresentMode,FrameType";

    public static string FormatSample(GameFrameSample sample)
    {
        StringBuilder builder = new(384);
        builder.Append(sample.CaptureSessionId.ToString("N", CultureInfo.InvariantCulture)).Append(',');
        AppendCsv(builder, sample.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(',').Append(sample.ProcessId.ToString(CultureInfo.InvariantCulture)).Append(',');
        AppendCsv(builder, sample.ProcessName);
        builder.Append(',');
        AppendCsv(builder, sample.SwapChainAddress);
        builder.Append(',').Append(FormatNumber(sample.Fps));
        builder.Append(',').Append(FormatNumber(sample.FrameTimeMs));
        builder.Append(',').Append(FormatNumber(sample.CpuBusyMs));
        builder.Append(',').Append(FormatNumber(sample.CpuWaitMs));
        builder.Append(',').Append(FormatNumber(sample.GpuLatencyMs));
        builder.Append(',').Append(FormatNumber(sample.GpuTimeMs));
        builder.Append(',').Append(FormatNumber(sample.GpuBusyMs));
        builder.Append(',').Append(FormatNumber(sample.GpuWaitMs));
        builder.Append(',').Append(FormatNumber(sample.RenderLatencyMs));
        builder.Append(',').Append(FormatNumber(sample.DisplayLatencyMs));
        builder.Append(',').Append(FormatNumber(sample.DisplayedTimeMs));
        builder.Append(',').Append(FormatNumber(sample.ClickToPhotonLatencyMs));
        builder.Append(',');
        AppendCsv(builder, sample.Runtime);
        builder.Append(',');
        AppendCsv(builder, sample.PresentMode);
        builder.Append(',');
        AppendCsv(builder, sample.FrameType);
        return builder.ToString();
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    public static bool TryGetDoubleField(string line, int fieldIndex, out double value)
    {
        ReadOnlySpan<char> span = line.AsSpan();
        int currentField = 0;
        int fieldStart = 0;
        bool quoted = false;
        for (int index = 0; index <= span.Length; index++)
        {
            bool atEnd = index == span.Length;
            char character = atEnd ? '\0' : span[index];
            if (!atEnd && character == '"')
            {
                if (quoted && index + 1 < span.Length && span[index + 1] == '"')
                {
                    index++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (!atEnd && (character != ',' || quoted))
            {
                continue;
            }

            if (currentField == fieldIndex)
            {
                ReadOnlySpan<char> field = span[fieldStart..index].Trim().Trim('"');
                return double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && double.IsFinite(value);
            }

            currentField++;
            fieldStart = index + 1;
        }

        value = 0d;
        return false;
    }

    private static string FormatNumber(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value)
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static void AppendCsv(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }
}
