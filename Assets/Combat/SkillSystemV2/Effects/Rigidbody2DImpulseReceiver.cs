using System.Collections;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Rigidbody2DImpulseReceiver : MonoBehaviour,
        ISpellImpulseReceiver
    {
        [SerializeField]
        private Rigidbody2D body;

        [SerializeField, Min(0f)]
        private float magnitudeMultiplier = 1f;

        private void Awake()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();
        }

        public bool TryReceiveImpulse(in SpellImpulseRequest request)
        {
            if (body == null ||
                body.bodyType != RigidbodyType2D.Dynamic ||
                request.Direction.sqrMagnitude <= 0.000001f ||
                request.Magnitude <= 0f)
            {
                return false;
            }

            float scaledMagnitude = request.Magnitude *
                                    Mathf.Max(0f, magnitudeMultiplier);

            switch (request.Mode)
            {
                case SpellImpulseMode.InstantVelocityChange:
                    body.linearVelocity += request.Direction * scaledMagnitude;
                    break;

                case SpellImpulseMode.Force:
                    if (request.Duration > 0f)
                    {
                        StartCoroutine(ApplyForceOverTime(
                            request.Direction * scaledMagnitude,
                            request.Duration));
                    }
                    else
                    {
                        body.AddForce(
                            request.Direction * scaledMagnitude,
                            ForceMode2D.Force);
                    }
                    break;

                default:
                    body.AddForce(
                        request.Direction * scaledMagnitude,
                        ForceMode2D.Impulse);
                    break;
            }

            return true;
        }

        private IEnumerator ApplyForceOverTime(
            Vector2 force,
            float duration)
        {
            float remaining = duration;
            var wait = new WaitForFixedUpdate();

            while (remaining > 0f && body != null)
            {
                body.AddForce(force, ForceMode2D.Force);
                remaining -= Time.fixedDeltaTime;
                yield return wait;
            }
        }

        private void OnValidate()
        {
            magnitudeMultiplier = Mathf.Max(0f, magnitudeMultiplier);
        }
    }
}
