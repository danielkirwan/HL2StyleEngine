
namespace Engine.Runtime.Entities.Interfaces
{
    public interface IComponentWithJson
    {
        void ApplyJson(string json);
        string ToJson();
    }
}
