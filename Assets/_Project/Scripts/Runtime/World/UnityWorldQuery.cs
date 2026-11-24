using System;
using Alesa.Core.AI;
using UnityEngine;
using CoreVec3 = System.Numerics.Vector3;

namespace Alesa.Runtime.World
{
    /// <summary>
    /// Unity implementation of <see cref="IWorldQuery"/>. Exposes player kinematics,
    /// physics line-of-sight checks, and approximate light-level sampling to engine-free brains.
    /// </summary>
    public sealed class UnityWorldQuery : IWorldQuery
    {
        private Transform _player;
        private CharacterController _playerCharacterController;
        private Rigidbody _playerRigidbody;
        private LayerMask _lineOfSightMask;
        private Light[] _cachedLights = Array.Empty<Light>();
        private Vector3 _previousPlayerPosition;
        private Vector3 _computedPlayerVelocity;
        private bool _hasPreviousPosition;

        public UnityWorldQuery(Transform player, LayerMask lineOfSightMask)
        {
            _lineOfSightMask = lineOfSightMask;
            SetPlayer(player);
            RefreshLights();
        }

        public float Time => UnityEngine.Time.time;

        public CoreVec3 PlayerPosition
        {
            get
            {
                ResolvePlayerIfMissing();
                return _player != null ? _player.position.ToCore() : CoreVec3.Zero;
            }
        }

        public CoreVec3 PlayerVelocity
        {
            get
            {
                ResolvePlayerIfMissing();
                if (_playerCharacterController != null) return _playerCharacterController.velocity.ToCore();
                if (_playerRigidbody != null) return _playerRigidbody.linearVelocity.ToCore();
                return _computedPlayerVelocity.ToCore();
            }
        }

        public CoreVec3 PlayerForward
        {
            get
            {
                ResolvePlayerIfMissing();
                return _player != null ? _player.forward.ToCore() : new CoreVec3(0f, 0f, 1f);
            }
        }

        public void SetPlayer(Transform player)
        {
            _player = player;
            _playerCharacterController = player != null ? player.GetComponent<CharacterController>() : null;
            _playerRigidbody = player != null ? player.GetComponent<Rigidbody>() : null;
            _hasPreviousPosition = player != null;
            _previousPlayerPosition = player != null ? player.position : Vector3.zero;
            _computedPlayerVelocity = Vector3.zero;
        }

        public void SetLineOfSightMask(LayerMask mask) => _lineOfSightMask = mask;

        public void RefreshLights()
        {
            _cachedLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        public void Tick(float deltaTime)
        {
            ResolvePlayerIfMissing();
            if (_player == null)
            {
                _computedPlayerVelocity = Vector3.zero;
                _hasPreviousPosition = false;
                return;
            }

            Vector3 currentPosition = _player.position;
            if (_hasPreviousPosition && deltaTime > 1e-5f)
            {
                _computedPlayerVelocity = (currentPosition - _previousPlayerPosition) / deltaTime;
            }
            else
            {
                _computedPlayerVelocity = Vector3.zero;
            }

            _previousPlayerPosition = currentPosition;
            _hasPreviousPosition = true;
        }

        public bool HasLineOfSight(CoreVec3 from, CoreVec3 to)
        {
            Vector3 start = from.ToUnity();
            Vector3 end = to.ToUnity();
            if ((end - start).sqrMagnitude <= 1e-6f) return true;
            return !Physics.Linecast(start, end, _lineOfSightMask, QueryTriggerInteraction.Ignore);
        }

        public float LightLevelAt(CoreVec3 position)
        {
            Vector3 point = position.ToUnity();
            float illumination = RenderSettings.ambientLight.grayscale * RenderSettings.ambientIntensity;

            for (int i = 0; i < _cachedLights.Length; i++)
            {
                Light light = _cachedLights[i];
                if (light == null || !light.isActiveAndEnabled || light.intensity <= 0f) continue;

                float contribution = EvaluateLightContribution(light, point);
                illumination += contribution;
                if (illumination >= 1f) return 1f;
            }

            return Mathf.Clamp01(illumination);
        }

        private float EvaluateLightContribution(Light light, Vector3 point)
        {
            float basePower = light.color.grayscale * light.intensity;

            switch (light.type)
            {
                case LightType.Directional:
                    return basePower * 0.5f;

                case LightType.Point:
                case LightType.Spot:
                {
                    if (light.range <= 0f) return 0f;

                    Vector3 lightPos = light.transform.position;
                    Vector3 toPoint = point - lightPos;
                    float dist = toPoint.magnitude;
                    if (dist >= light.range) return 0f;

                    float attenuation = 1f - (dist / light.range);
                    attenuation *= attenuation;

                    if (light.type == LightType.Spot && dist > 1e-4f)
                    {
                        float halfAngle = light.spotAngle * 0.5f;
                        float angle = Vector3.Angle(light.transform.forward, toPoint / dist);
                        if (angle > halfAngle) return 0f;
                        attenuation *= 1f - (angle / Mathf.Max(1f, halfAngle));
                    }

                    if (dist > 0.05f && Physics.Linecast(lightPos, point, _lineOfSightMask, QueryTriggerInteraction.Ignore))
                    {
                        return 0f;
                    }

                    return basePower * attenuation;
                }

                default:
                    return 0f;
            }
        }

        private void ResolvePlayerIfMissing()
        {
            if (_player != null) return;
            GameObject taggedPlayer = GameObject.FindWithTag("Player");
            if (taggedPlayer != null)
            {
                SetPlayer(taggedPlayer.transform);
            }
        }
    }
}
