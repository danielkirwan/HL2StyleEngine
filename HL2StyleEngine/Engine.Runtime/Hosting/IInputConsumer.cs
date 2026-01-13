using Engine.Input.Devices;

namespace Engine.Runtime.Hosting;

public interface IInputConsumer
{
    InputState InputState { get; }
}
