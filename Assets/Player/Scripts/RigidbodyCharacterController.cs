using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RigidbodyCharacterController : MonoBehaviour
{
    [Tooltip("Maximum slope the character can jump on")]
    [Range(5f, 60f)]
    [SerializeField] float slopeLimit = 45f;
    [Tooltip("Move speed in meters/second")]
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float moveAcceleration = 5f;
    [Tooltip("Turn speed multiplier")]
    [SerializeField] float turnSpeed = 100f;
    [Tooltip("Whether the character can jump")]
    [SerializeField] bool allowJump = false;
    [Tooltip("Upward speed to apply when jumping in meters/second")]
    [SerializeField] float jumpSpeed = 4f;

    [SerializeField] float groundingSpring = 50;
    [SerializeField] float groundingDamp = 10;

    public bool IsGrounded { get; private set; }
    public bool IsJumping { get; private set; }
    public float ForwardInput { get; set; }
    public float RightInput { get; set; }
    public Vector2 TurnInput { get; set; }
    public bool JumpInput { get; set; }

    new public Rigidbody rigidbody;
    private CapsuleCollider capsuleCollider;
    public new Camera camera;

    private static bool noclip;

    private void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.N)) noclip = !noclip;
    }

    private void FixedUpdate()
    {
        // Get input values
        float vertical = Input.GetAxisRaw("Vertical");
        float horizontal = Input.GetAxisRaw("Horizontal");
        bool jump = Input.GetKey(KeyCode.Space);
        ForwardInput = vertical;
        RightInput = horizontal;
        TurnInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        JumpInput = jump;

        UpdateGrounding();
        UpdateMovement();
    }

    static readonly Vector3[] groundingRays = new Vector3[]
    {
        new Vector3(0,0,0),
        new Vector3(1,0,0),
        new Vector3(-1,0,0),
        new Vector3(0,0,1),
        new Vector3(0,0,-1),
    };

    Vector3 gravity => Physics.gravity;
    float verticalGravity => transform.InverseTransformVector(Physics.gravity).y;

    private void UpdateGrounding()
    {
        if (noclip) return;
        IsGrounded = false;
        float capsuleRadius = capsuleCollider.radius;
        float capsuleHeight = Mathf.Max(capsuleCollider.radius * 2f, capsuleCollider.height);
        Vector3 capsuleBottom = capsuleCollider.center - Vector3.up * (capsuleHeight / 2f  - capsuleRadius);
        RaycastHit hit;
        float groundHeight = 0;
        int groundHitCount = 0;
        for (int i = 0; i < groundingRays.Length; i++)
        {
            Ray ray = new Ray(transform.TransformPoint(capsuleBottom + groundingRays[i] * capsuleRadius * 0.8f), -transform.up);
            if (Physics.Raycast(ray, out hit, capsuleBottom.y * 1.25f))
            {
                groundHitCount++;
                groundHeight += capsuleBottom.y - hit.distance;
            }
        }
        IsGrounded = groundHitCount > 0;
        float groundStrength = groundHitCount / (float)groundingRays.Length;

        if (IsGrounded && !IsJumping)
        { // soft grounding force
            groundHeight /= groundHitCount;

            float spring = Mathf.Max(groundingSpring * groundHeight - verticalGravity, 0);
            float damp = groundingDamp * -Vector3.Dot(transform.up, rigidbody.velocity) * (groundHeight + 1);

            rigidbody.AddRelativeForce(Vector3.up * (spring + damp) * groundStrength, ForceMode.Acceleration);
        }
    }

    /// <summary>
    /// Processes input actions and converts them into movement
    /// </summary>
    private void UpdateMovement()
    {
        transform.Rotate(Vector3.up, Time.fixedDeltaTime * TurnInput.x * turnSpeed);
        float verticalLook = Mathf.Repeat(camera.transform.localEulerAngles.x + 180, 360) - 180;
        verticalLook -= Time.fixedDeltaTime * TurnInput.y * turnSpeed;
        verticalLook = Mathf.Clamp(verticalLook, -90, 90);
        camera.transform.localRotation = Quaternion.Euler(verticalLook, 0, 0);

        // rotate to match gravity up
        Vector3 forward = transform.forward;
        forward = Vector3.ProjectOnPlane(forward, gravity);
        Quaternion upright = Quaternion.LookRotation(forward, -gravity);
        transform.rotation = Quaternion.Lerp(transform.rotation, upright, Time.fixedDeltaTime * 6);

        rigidbody.detectCollisions = !noclip;
        if (noclip)
        {
            Vector3 vel = camera.transform.InverseTransformVector(rigidbody.velocity);
            vel = Vector3.MoveTowards(vel, new Vector3(RightInput * moveSpeed, 0, ForwardInput * moveSpeed) * 3, Time.fixedDeltaTime * moveAcceleration);
            rigidbody.velocity = camera.transform.TransformVector(vel);
            rigidbody.AddForce(-Physics.gravity, ForceMode.Acceleration);
        }
        else
        {
            // Process Movement/Jumping
            if (IsGrounded)
            {
                Vector3 vel = transform.InverseTransformVector(rigidbody.velocity);

                float yvel = vel.y;
                vel.y = 0;
                vel = Vector3.MoveTowards(vel, new Vector3(RightInput * moveSpeed, 0, ForwardInput * moveSpeed), Time.fixedDeltaTime * moveAcceleration);
                vel.y = yvel;
                rigidbody.velocity = transform.TransformVector(vel);

                if (JumpInput && allowJump && !IsJumping)
                {
                    // Apply an upward velocity to jump
                    rigidbody.velocity += transform.up * jumpSpeed;
                    IsJumping = true;
                }
            }
        }

        if (IsJumping && Vector3.Dot(rigidbody.velocity, transform.up) < 0) IsJumping = false;
    }
}
