using Eto.Drawing;
using Eto.Forms;
namespace Aholo.Rhino;

/// <summary>Shared narrow-window layouts. Native resize/keyboard behavior is a host acceptance gate.</summary>
internal static class UiLayout
{
    internal static string Caption(string identity)=>Aholo.Core.UiText.Get(identity switch
    {
        "Create · Uses service credits" => "Generate · Uses credits",
        "Upload selection and render · Uses service credits" => "Render selection · Uses credits",
        "Continue original render · Uses service credits" => "Continue render · Uses credits",
        "Refresh selected task" => "Refresh task",
        "Download selected result" => "Download result",
        "Place in current document" => "Place in document",
        "Preview mesh candidate" => "Preview candidate",
        "Cloud history and recovery" => "Cloud history / recover…",
        "Disconnect and clear credentials" => "Disconnect & clear key",
        "Restore saved connection" => "Restore saved key",
        "Use current construction plane" => "Use current CPlane",
        "Export selected scene USD" => "Export scene USD",
        "Export selected mesh USD" => "Export mesh USD",
        _=>identity
    });
    internal static Label Text(string text)=>new(){Text=text,Wrap=WrapMode.Word,Width=1};
    internal static StackLayout Stack(params Control[] controls)
    {
        var stack=new StackLayout{Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        foreach(var control in controls)stack.Items.Add(control);
        return stack;
    }
    // Action groups deliberately keep a stable order and orientation while resizing.
    internal static Control Actions(params Control[] controls)=>Stack(controls);
    internal static Control Toolbar(params Control[] controls)
    {
        var row=new TableRow();
        for(int i=0;i<controls.Length;i++)row.Cells.Add(new TableCell(controls[i],i==0));
        return new TableLayout(row){Spacing=new Size(6,6)};
    }
    internal static Scrollable Scroll(params Control[] controls)=>new FittedScroll(Stack(controls));
    internal static Control DialogBody(Control body,Button close)=>new StackLayout
    {Padding=12,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={new StackLayoutItem(body,true),close}};
    private sealed class FittedScroll : Scrollable
    {
        private readonly Control body;
        private bool queued,disposed;
        private int assigned=-1;
        internal FittedScroll(Control body)
        {
            this.body=body;Content=body;Border=BorderType.None;ExpandContentWidth=true;
            Load+=(_,_)=>QueueFit();SizeChanged+=(_,_)=>QueueFit();body.SizeChanged+=(_,_)=>QueueFit();
        }
        private void QueueFit()
        {
            if(queued||disposed)return;queued=true;
            Application.Instance.AsyncInvoke(()=>
            {
                queued=false;if(disposed)return;
                // VisibleRect is the actual clip viewport; ClientSize is the pre-layout fallback.
                int width=VisibleRect.Width>0?VisibleRect.Width:ClientSize.Width;
                if(width<=0)return;
                // Below the supported body width, preserve access through native horizontal scrolling.
                int target=Math.Max(280,width);
                if(target==assigned)return;assigned=target;body.Width=target;
            });
        }
        protected override void Dispose(bool disposing){disposed=true;base.Dispose(disposing);}
    }
}
