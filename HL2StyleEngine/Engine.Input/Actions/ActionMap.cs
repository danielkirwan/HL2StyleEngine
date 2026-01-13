using Veldrid;

namespace Engine.Input.Actions;

/// <summary>
/// A collection of actions + their bindings.
/// </summary>
public sealed class ActionMap
{
    private readonly Dictionary<string, InputAction> _actions = new();
    private readonly Dictionary<InputAction, List<InputBinding>> _bindings = new();

    public IEnumerable<InputAction> Actions => _actions.Values;

    public InputAction AddAction(string name)
    {
        if (_actions.ContainsKey(name))
            throw new InvalidOperationException($"Action '{name}' already exists.");

        var action = new InputAction(name);
        _actions[name] = action;
        _bindings[action] = new List<InputBinding>();
        return action;
    }

    public InputAction Get(string name)
    {
        if (_actions.TryGetValue(name, out var a))
            return a;

        throw new KeyNotFoundException($"Action '{name}' not found.");
    }

    public void BindKey(InputAction action, Key key)
        => _bindings[action].Add(InputBinding.FromKey(key));

    public void BindMouse(InputAction action, MouseButton button)
        => _bindings[action].Add(InputBinding.FromMouse(button));

    internal IReadOnlyList<InputBinding> GetBindings(InputAction action)
        => _bindings[action];
}
