using Godot;
using System;

namespace Pastures.Game;

public partial class CameraController : Camera3D
{
    [Export] public NodePath PaperAirplane { get; set; }
    private Node3D _paperAirplane;

    [Export] public Vector3 LookAtOffset { get; set; } = new Vector3(0f, 0.5f, 0f);
    [Export] public float HeightOffset = 1.5f;
    [Export] public float RadiusOffset = 2.0f;
    [Export] public float FollowSpeed { get; set; } = 5.0f;

	private float _rawYawAngles = 0f;
    private float _yawAngles = 0f;

    private Transform3D _interpPrevious, _interpCurrent;
    private bool _interpUpdate = false;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _paperAirplane = GetNode<Node3D>(PaperAirplane);

        // Initialise variables.
        _interpPrevious = _interpCurrent = _paperAirplane.GlobalTransform;
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
		// Interpolate yaw angles for smooth yaw look.
		_yawAngles = Mathf.LerpAngle(_yawAngles, _rawYawAngles, (float)delta * 5.0f);

		// Marked dirty if there's been a physics update.
        if (_interpUpdate)
        {
			// Update current understanding of plane transform, and cache old value.
            _interpPrevious = _interpCurrent;
            _interpCurrent = _paperAirplane.GlobalTransform;

			// Mark clean.
            _interpUpdate = false;
        }

		// Get interpolation transform.
		// This smoothly interpolates between frames to allow the camera to know where the aeroplane would be on this frame.
        float interpFraction = Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0f, 1f);

		// Interpolate between the previous and current transforms based on where we are in relation to the next physics step.
        Transform3D interpPlane = _interpPrevious.InterpolateWith(_interpCurrent, interpFraction);

		// Store plane transform as the camera's new transform.
		Transform3D interpCamera = interpPlane;

		// Apply new position.
		interpCamera.Origin += new Vector3
        (
            Mathf.Sin(_yawAngles) * RadiusOffset,
            HeightOffset,
            Mathf.Cos(_yawAngles) * RadiusOffset
        );

		// Update global transform to the interpCamera transform looking at the plane with an offset.
		GlobalTransform = interpCamera.LookingAt(interpPlane.Origin + LookAtOffset);
    }

    public override void _PhysicsProcess(double delta)
    {
        _interpUpdate = true;
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion mm:
                _rawYawAngles -= mm.Relative.X * 0.01f;
                _rawYawAngles += _rawYawAngles < 0f ? Mathf.Tau : _rawYawAngles > Mathf.Tau ? -Mathf.Tau : 0f;
                break;
        }
    }
}
