namespace Engine.Core.Time;

public sealed class FixedTimestep
{
    private float _accumulator;

    public void Update(float deltaTime, Action fixedUpdate)
    {
        _accumulator += deltaTime;

        while (_accumulator >= Time.FixedDeltaTime)
        {
            fixedUpdate();
            _accumulator -= Time.FixedDeltaTime;
            Time.TotalTime += Time.FixedDeltaTime;
        }
    }
}
