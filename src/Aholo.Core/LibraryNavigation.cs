namespace Aholo.Core;

public sealed record LibraryRead(long NavigationRevision,long LoadRevision,TaskPageRequest Page,Guid? RevealTaskId,Guid? SelectedTaskId);

/// <summary>UI-thread state: background refreshes inherit an unfinished explicit reveal.</summary>
public sealed class LibraryNavigation
{
    private long navigationRevision,loadRevision;
    private Guid? pendingReveal;
    public void Reveal(Guid taskId)
    {
        if(taskId==Guid.Empty)throw new ArgumentException("A task identity is required.",nameof(taskId));
        navigationRevision++;pendingReveal=taskId;
    }
    public void UserNavigated(){navigationRevision++;pendingReveal=null;}
    public LibraryRead BeginRead(TaskPageRequest page,Guid? selectedTaskId)
        =>new(navigationRevision,++loadRevision,page,pendingReveal,pendingReveal??selectedTaskId);
    public bool IsCurrent(LibraryRead read)=>read.NavigationRevision==navigationRevision&&read.LoadRevision==loadRevision;
    public bool TryApply(LibraryRead read,TaskPage page,out Guid? selection)
    {
        selection=null;if(!IsCurrent(read))return false;
        if(read.RevealTaskId is{} target&&!page.Items.Any(x=>x.Id==target))
            throw new IOException("The requested local task was not found.");
        selection=page.Items.Any(x=>x.Id==read.SelectedTaskId)?read.SelectedTaskId:page.Items.FirstOrDefault()?.Id;
        pendingReveal=null;loadRevision++;return true;
    }
}
