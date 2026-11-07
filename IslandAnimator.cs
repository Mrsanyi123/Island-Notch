using System;
using System.Diagnostics;
using System.Windows.Media;
namespace Island;
internal sealed class IslandAnimator
{
    private readonly Action<double,double,double> render;
    private double width = 200, height = 36, radius = 18, vw, vh, vr;
    private double tw = 200, th = 36, tr = 18;
    private bool running;
    private readonly Stopwatch watch = new();
    public IslandAnimator(Action<double,double,double> render) => this.render = render;
    public void Target(double w, double h, double r)
    {
        tw=w; th=h; tr=r;
        if (running) return;
        running=true; watch.Restart(); CompositionTarget.Rendering += Tick;
    }
    private void Tick(object? sender, EventArgs args)
    {
        double dt=Math.Min(watch.Elapsed.TotalSeconds, .032); watch.Restart();
        // Small integration steps keep the spring stable even after a slow frame.
        int steps=Math.Max(1,(int)Math.Ceiling(dt/.008));
        for(int i=0;i<steps;i++) { Step(ref width,ref vw,tw,dt/steps); Step(ref height,ref vh,th,dt/steps); Step(ref radius,ref vr,tr,dt/steps); }
        bool done=Math.Abs(width-tw)+Math.Abs(height-th)+Math.Abs(radius-tr)+Math.Abs(vw)+Math.Abs(vh)+Math.Abs(vr)<.12;
        if(done) { width=tw;height=th;radius=tr; Stop(); }
        render(width,height,radius);
    }
    private static void Step(ref double x,ref double v,double target,double dt) { v+=((target-x)*240-v*31)*dt; /* zeta ~1: critically damped, no overshoot/wobble */ x+=v*dt; }
    public void Stop() { CompositionTarget.Rendering-=Tick;running=false;watch.Stop(); }
}
