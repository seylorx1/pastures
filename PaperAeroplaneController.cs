using Godot;
using System;

namespace Pastures.Game;

public partial class PaperAeroplaneController : RigidBody3D
{
    [Export] public NodePath MeshInstance;

    private MeshInstance3D _meshInstance;
    private Transform3D _interpPrevious, _interpCurrent;
    private bool _interpUpdate = false;

	Vector3 _rawEuler = Vector3.Zero;


    public override void _Ready()
    {
        _meshInstance = GetNode<MeshInstance3D>(MeshInstance);
        _meshInstance.TopLevel = true;

        _meshInstance.GlobalTransform = _interpPrevious = _interpCurrent = GlobalTransform;
    }

    public override void _Process(double delta)
    {
        if (_interpUpdate)
        {
            _interpPrevious = _interpCurrent;
            _interpCurrent = GlobalTransform;

            _interpUpdate = false;
        }

        float interpFraction = Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0f, 1f);
        _meshInstance.GlobalTransform = _interpPrevious.InterpolateWith(_interpCurrent, interpFraction);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = _interpCurrent.Origin - _interpPrevious.Origin;

        if (Input.IsActionJustPressed("plane_up") && velocity.Y <= 0f)
        {
            ApplyCentralImpulse(Vector3.Up * 0.2f);
        }

		if (Input.IsActionPressed("plane_left"))
		{
			_rawEuler.Y += Mathf.Pi * 0.2f * (float)delta;
			_rawEuler.Z = Mathf.Pi * 0.3f;
		}
		else
		{
			_rawEuler.Z = 0f;
		}

		if (Input.IsActionPressed("plane_right"))
		{
			_rawEuler.Y -= Mathf.Pi * 0.2f * (float)delta;
			_rawEuler.Z -= Mathf.Pi * 0.3f;
		}

		// Simple deadzone.
		if(Mathf.Abs(velocity.Y) < 0.1f)
			_rawEuler.X = 0f;
		else if(velocity.Y > 0f)
			_rawEuler.X = velocity.Y - 0.1f;
		else
			_rawEuler.X = velocity.Y + 0.1f;

		// Clamps extent angles to 0.2.
		_rawEuler.X = Mathf.Clamp(_rawEuler.X, -0.15f, 0.15f) / 0.15f;

		// Convert to angles...
		_rawEuler.X *= Mathf.Pi * 0.45f;

		Rotation = new
		(
			Mathf.LerpAngle(Rotation.X, _rawEuler.X, (float)delta),
			Mathf.LerpAngle(Rotation.Y, _rawEuler.Y, (float)delta),
			Mathf.LerpAngle(Rotation.Z, _rawEuler.Z, (float)delta)
		);

        _interpUpdate = true;
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
		LinearVelocity = new Vector3(Mathf.Sin(Rotation.Y) * -10.0f, LinearVelocity.Y, Mathf.Cos(Rotation.Y) * -10.0f);
        base._IntegrateForces(state);
    }
}
