using System.Numerics;
using Engine.Physics.Dynamics;
using Engine.Runtime.Entities;
using Game.Puzzles;

namespace Game;

public sealed partial class HL2GameModule
{
    private readonly List<(Entity Entity, PressurePlateSensor Sensor)> _pressurePlates = new();
    private readonly List<Entity> _puzzleIndicators = new();
    private readonly HashSet<string> _activePuzzleStates = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterPuzzleMechanisms()
    {
        _pressurePlates.Clear();
        _puzzleIndicators.Clear();
        _activePuzzleStates.Clear();
        foreach (Entity entity in _runtimeEntities)
        {
            if (IsInteractionKind(entity, "PressurePlate"))
                _pressurePlates.Add((entity, new PressurePlateSensor()));
            if (IsInteractionKind(entity, "PuzzleIndicator"))
                _puzzleIndicators.Add(entity);
        }
    }

    private void UpdatePressurePlates(float dt)
    {
        foreach (var (plate, sensor) in _pressurePlates)
        {
            var settings = GetInteraction(plate)!;
            float mass = 0;
            foreach (Entity body in _runtimeEntities)
            {
                if (body.Physics.MotionType != MotionType.Dynamic || body.IsBroken ||
                    body.Render.Shape == RuntimeShapeKind.None || !TryGetPhysicsBodyAabb(body, out var bounds))
                    continue;
                if (PressurePlateSensor.SupportsBody(plate.Transform.Position, plate.Collider.Size,
                    GetColliderRotation(plate), bounds, body.Transform.Position, GetPhysicsBodyVelocity(body), body.IsHeld))
                    mass += GetPhysicsBodyMass(body);
            }
            sensor.Update(mass, settings.PressurePlateMinMass, settings.PressurePlateSettleSeconds, dt);
            string state = GetInteractionStateId(plate);
            if (sensor.Active) _activePuzzleStates.Add(state);
            else _activePuzzleStates.Remove(state);
        }
        UpdatePuzzleIndicators();
    }

    private bool IsPuzzleRequirementComplete(string state)
        => _activePuzzleStates.Contains(state) || _solvedPuzzles.Contains(state) ||
           _openedDoors.Contains(state) || _collectedInteractables.Contains(state);

    private void UpdatePuzzleIndicators()
    {
        foreach (Entity indicator in _puzzleIndicators)
            indicator.Render.Color = AreInteractionRequiredStatesComplete(indicator)
                ? new Vector4(0.12f, 0.9f, 0.32f, 1)
                : new Vector4(0.9f, 0.13f, 0.06f, 1);
    }

    private Vector3 PuzzleDoorOpenPosition(Entity door, Vector3 closedPosition)
    {
        float height = GetInteraction(door)?.LiftHeight ?? PuzzleDoorLiftHeight;
        if (!float.IsFinite(height) || height <= 0) height = PuzzleDoorLiftHeight;
        return closedPosition + Vector3.UnitY * height;
    }
}
