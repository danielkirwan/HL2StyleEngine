namespace Engine.Core.Time;

public sealed class FixedTimestep
{
    private float _accumulator;
    public int LastStepCount { get; private set; }
    public float AccumulatorSeconds => _accumulator;

    public void Update(float deltaTime, Action fixedUpdate)
    {
        LastStepCount = 0;
        _accumulator += deltaTime;

        while (_accumulator >= Time.FixedDeltaTime)
        {
            fixedUpdate();
            LastStepCount++;
            _accumulator -= Time.FixedDeltaTime;
            Time.TotalTime += Time.FixedDeltaTime;
        }
    }
}
