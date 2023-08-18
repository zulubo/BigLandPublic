using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShipPilotInteraction : FirstPersonInteraction
{
    public Ship ship;

    [SerializeField] Transform pilotView;
    [SerializeField] Transform exitSeatPose;

    [System.Serializable]
    public class SpringSim
    {
        public float springCoefficient = 5;
        public float dampCoefficient = 2;
        public float accelerationMultiplier = 1;
        public float clampDistance = 0.5f;

        [HideInInspector]
        public Vector3 position;
        private Vector3 velocity;

        private Vector3 oldFrameVel;
        public void SetFrameVelocity(Vector3 vel, float deltaTime)
        {
            Vector3 acc = (oldFrameVel - vel) / deltaTime;
            oldFrameVel = vel;

            velocity -= accelerationMultiplier * deltaTime * acc;
        }

        public void Update(float deltaTime)
        {
            float clamping = 0;
            if (clampDistance > 0 && Vector3.Dot(velocity, position) > 0)
            {
                clamping = position.sqrMagnitude / (clampDistance * clampDistance);
            }
            velocity -= Mathf.Clamp01(springCoefficient * deltaTime) * position;
            velocity -= Mathf.Clamp01(dampCoefficient * deltaTime * (1 + clamping)) * velocity;
            position += velocity * deltaTime;
        }

        public void Reset()
        {
            oldFrameVel = Vector3.zero;
            velocity = Vector3.zero;
            position = Vector3.zero;
        }
    }

    public SpringSim viewSpring;

    public override Transform localSpace => transform;

    protected override void BeginInteraction() 
    {
        viewSpring.Reset();
    }

    private void FixedUpdate()
    {
        if (interacting)
        {
            viewSpring.SetFrameVelocity(ship.rigidbody.GetPointVelocity(pilotView.position), Time.fixedDeltaTime);
            viewSpring.Update(Time.fixedDeltaTime);
        }
    }

    protected override bool FinalCharacterPose(out Vector3 position, out Vector3 forward)
    {
        position = exitSeatPose.position;
        forward = exitSeatPose.forward;
        return true;
    }

    protected override Pose GetViewPose()
    {
        Vector3 viewRot = new Vector3(Vector3.Dot(pilotView.transform.up, viewSpring.position), 
            Vector3.Dot(-pilotView.transform.right, viewSpring.position), 0);
        viewRot.z = viewRot.x * -0.5f;
        return new Pose(pilotView.position, pilotView.rotation * Quaternion.Euler(viewRot * 10));
    }

    protected override bool InteractionAllowed()
    {
        return true;
    }

    protected override bool IsInteractionFinished()
    {
        return Input.GetKeyDown(KeyCode.Space);
    }
}
