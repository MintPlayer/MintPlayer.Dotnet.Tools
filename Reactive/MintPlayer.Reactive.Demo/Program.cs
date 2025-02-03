using System.Reactive.Linq;
using System.Reactive.Subjects;

var test = new Test();
var obs = Observable.FromEventPattern<Test.ClickEventHandler, ClickEventArgs>(
    handler => test.Click += handler,
    handler => test.Click -= handler);

var destroyed = new Subject<bool>();

obs.TakeUntil(destroyed).Subscribe(evt =>
{
    Console.WriteLine("Button clicked");
});

test.OnClick(new ClickEventArgs { Button = 1 });
test.OnClick(new ClickEventArgs { Button = 2 });
destroyed.OnNext(true);
test.OnClick(new ClickEventArgs { Button = 3 });
test.OnClick(new ClickEventArgs { Button = 4 });



public class ClickEventArgs : EventArgs
{
    public int Button { get; set; }
}

public class Test
{
    public delegate void ClickEventHandler(object sender, ClickEventArgs e);
    public event ClickEventHandler Click;

    public void OnClick(ClickEventArgs e)
    {
        if (Click != null) Click(this, e);
    }
}
