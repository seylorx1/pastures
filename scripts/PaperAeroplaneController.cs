using Godot;
using System;
using System.IO;

namespace Pastures.Game;

public partial class PaperAeroplaneController : RigidBody3D
{
    [Export] public NodePath MeshInstance;

    private MeshInstance3D _meshInstance;
    private Transform3D _interpPrevious, _interpCurrent;
    private bool _interpUpdate = false;

	private Vector3 _rawEuler = Vector3.Zero;

    private float _rawVertical = 0f;
    private float _vertical = 0f;


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

        if (Input.IsActionPressed("plane_up"))
        {
            _rawVertical = 1f;
            _rawEuler.X = Mathf.Pi * 0.4f;
        }
        else if(Input.IsActionPressed("plane_down"))
        {
            _rawVertical = -2f;
            _rawEuler.X = -Mathf.Pi * 0.4f;
        }
        else
        {
            _rawVertical = -0.5f;
            _rawEuler.X = 0f;
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

		Rotation = new
		(
			Mathf.LerpAngle(Rotation.X, _rawEuler.X, (float)delta),
			Mathf.LerpAngle(Rotation.Y, _rawEuler.Y, (float)delta),
			Mathf.LerpAngle(Rotation.Z, _rawEuler.Z, (float)delta)
		);

        _vertical = Mathf.Lerp(_vertical, _rawVertical, (float)delta);

        _interpUpdate = true;
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
		LinearVelocity = new Vector3
        (
            x: Mathf.Sin(Rotation.Y) * -10.0f,
            y: _vertical * 10.0f,
            z: Mathf.Cos(Rotation.Y) * -10.0f
        );
        base._IntegrateForces(state);
    }
}
