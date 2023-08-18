using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ship : MonoBehaviour
{
    [SerializeField] float thrust = 15;
    [SerializeField] float throttleSpeed = 0.3f;
    [SerializeField] float angularThrust;
    [SerializeField] float turnSpeed = 10;
    [SerializeField] Vector3 drag;

    [SerializeField] ShipPilotInteraction interaction;
    public bool isBeingPiloted => interaction.interacting;

    private float throttle;

    [HideInInspector]
    new public Rigidbody rigidbody;

    private void Start()
    {
        rigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (isBeingPiloted)
        {
            throttle = Mathf.Clamp01(Input.GetAxis("Vertical"));// throttle + Input.GetAxis("Vertical") * Time.fixedDeltaTime * throttleSpeed);
            rigidbody.AddForce(transform.up * throttle * thrust, ForceMode.Acceleration);

            Vector3 localvel = transform.InverseTransformVector(rigidbody.velocity);
            Vector3 dragForce = -Vector3.Scale(localvel, drag);
            rigidbody.AddRelativeForce(dragForce, ForceMode.Acceleration);

            Vector3 desiredTurn = new Vector3(Input.GetAxis("Mouse Y"), Input.GetAxis("Horizontal"), -Input.GetAxis("Mouse X")) * turnSpeed;
            Vector3 turn = desiredTurn - transform.InverseTransformVector(rigidbody.angularVelocity);
            turn.x = Mathf.Clamp(turn.x, -angularThrust, angularThrust);
            turn.y = Mathf.Clamp(turn.y, -angularThrust, angularThrust);
            turn.z = Mathf.Clamp(turn.z, -angularThrust, angularThrust);
            rigidbody.AddRelativeTorque(turn, ForceMode.Acceleration);
        }
        else
        {
            throttle = 0;
        }


    }
}
