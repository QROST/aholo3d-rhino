using System.Globalization;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class TaskProgressPresentationTests
{
    private static LocalTask Running(double? progress)=>new(){Kind="world",Status="RUNNING",WorldId="world",Progress=progress};
    [Theory]
    [InlineData(.457,"45.7")][InlineData(.999999,"99.9")][InlineData(1,"100")][InlineData(.001,"0.1")]
    public void FractionalServiceProgressIsAPercentageWithoutPrematureCompletion(double value,string expected)
    {Assert.Equal("Service progress: "+expected+"%",TaskProgressPresentation.Progress(Running(value),"en",CultureInfo.InvariantCulture));}
    [Fact]
    public void ZeroIsWaitingAndInvalidOrMissingValuesNeverBecomeFakeProgress()
    {
        Assert.Equal("Waiting for a service progress update.",TaskProgressPresentation.Progress(Running(0),"en"));
        foreach(double? value in new double?[]{null,-1,2,double.NaN,double.PositiveInfinity})Assert.Equal("Service progress is unavailable.",TaskProgressPresentation.Progress(Running(value),"en"));
        Assert.Equal("Service progress is unavailable.",TaskProgressPresentation.Progress(Running(.6) with{Kind="object"},"en"));
    }
    [Theory]
    [InlineData("SUCCEEDED")][InlineData("FAILED")][InlineData("SubmissionUnknown")][InlineData("RecordError")]
    public void TerminalAndUncertainRecordsDoNotInventAnActiveProgressBar(string status)
    {
        var task=Running(.4) with{Status=status};Assert.Empty(TaskProgressPresentation.Progress(task,"en"));Assert.Empty(TaskProgressPresentation.RecordAge(task,DateTimeOffset.UtcNow,"en"));
    }
    [Fact]
    public void AgeIsSinceLocalRecordingNotAnEstimatedCloudCompletionTime()
    {
        var start=DateTimeOffset.Parse("2026-11-11T12:00:00Z",CultureInfo.InvariantCulture);var task=Running(0) with{CreatedAt=start};
        Assert.Equal("Time since recorded: 00:02:03",TaskProgressPresentation.RecordAge(task,start.AddSeconds(123),"en"));
        Assert.Equal("Time since recorded: 1.02:03:04",TaskProgressPresentation.RecordAge(task,start.AddDays(1).AddHours(2).AddMinutes(3).AddSeconds(4),"en"));
        Assert.Equal("Time since recorded: 00:00:00",TaskProgressPresentation.RecordAge(task,start.AddMinutes(-1),"en"));
    }
    [Fact]
    public void ProgressUsesChosenUiLanguageAndNumericCulture()
    {
        Assert.Equal("服务进度：45.7%",TaskProgressPresentation.Progress(Running(.457),"zh",CultureInfo.InvariantCulture));
        Assert.Equal("Service progress: 45,7%",TaskProgressPresentation.Progress(Running(.457),"en",CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("等待服务更新进度。",TaskProgressPresentation.Progress(Running(0),"zh"));
    }
}
