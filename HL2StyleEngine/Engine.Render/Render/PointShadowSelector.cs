using System.Numerics;

namespace Engine.Render;

public sealed class PointShadowSelector
{
    private Vector3? _previous;
    public int Select(IReadOnlyList<WorldPointLight> lights, Vector3 camera, float hysteresis)
    {
        int best=-1, prior=-1;
        for(int i=0;i<lights.Count;i++)
        {
            var light=lights[i];
            if (!light.CastShadows || light.SpotAngleDegrees>0 && light.Direction.LengthSquared()>.001f) continue;
            if(best<0)best=i;
            if(_previous==light.Position)prior=i;
        }
        if (best<0) { _previous=null; return -1; }
        float margin=float.IsFinite(hysteresis)?Math.Clamp(hysteresis,0,10):0;
        if (margin>0 && prior>=0 && lights[prior].Priority>=lights[best].Priority &&
            Vector3.Distance(camera,lights[prior].Position)<=Vector3.Distance(camera,lights[best].Position)+margin) best=prior;
        _previous=lights[best].Position; return best;
    }
}
