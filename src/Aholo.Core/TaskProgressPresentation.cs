using System.Globalization;
namespace Aholo.Core;

/// <summary>Reported service progress and local record age, without a synthesized percentage or ETA.</summary>
public static class TaskProgressPresentation
{
    public static string Progress(LocalTask task,string? language=null,CultureInfo? culture=null)
    {
        if(!TaskCapabilities.For(task).IsRunning)return "";
        if(task.Kind!="world"||task.Progress is not {} value||!double.IsFinite(value)||value<0||value>1)
            return UiText.Get("Service progress is unavailable.",language);
        if(value==0)return UiText.Get("Waiting for a service progress update.",language);
        // Do not round an in-progress value up to 100%.
        double percent=Math.Truncate(value*1000)/10;
        return string.Format(culture??CultureInfo.CurrentCulture,UiText.Get("Service progress: {0}%",language),percent.ToString("0.#",culture??CultureInfo.CurrentCulture));
    }
    public static string RecordAge(LocalTask task,DateTimeOffset now,string? language=null)
    {
        if(!TaskCapabilities.For(task).IsRunning)return "";
        var age=now>task.CreatedAt?now-task.CreatedAt:TimeSpan.Zero;
        string duration=age.ToString(age.Days>0?@"d\.hh\:mm\:ss":@"hh\:mm\:ss",CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.CurrentCulture,UiText.Get("Time since recorded: {0}",language),duration);
    }
}
