// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using UnityEngine;

namespace GPUInstancerPro
{
    public class GPUIObjectMover : MonoBehaviour
    {
        [Range(-100f, 100f)]
        public float forwardMove;
        [Range(-100f, 100f)]
        public float upwardMove;
        public Vector3 positionChange;
        public Vector3 rotationChange;
        public bool isRandomDirection;
        public bool isLooping;
        public float loopDistance;
        public float loopAngle;
        public bool loopChangeDirection;

        public bool isRayCasting;
        public float rayCastHeight = 15f;
        public int rayCastLayer = 6;
        public float rayCastMaxDistance = 200f;

        public bool isOrbiting;
        public Transform orbitCenter;
        public float orbitSpeed = 1f;

        private Transform _cachedTransform;
        private Vector3 _startPosition;
        private Vector3 _startEulerAngles;
        private Vector3 _endPosition;
        private Vector3 _endEulerAngles;
        private float _orbitDistance;

        private float _loopDuration;
        private float _startTime;

        private void OnEnable()
        {
            _cachedTransform = transform;
            _startPosition = _cachedTransform.position;
            _startEulerAngles = _cachedTransform.rotation.eulerAngles;

            if (isRandomDirection)
            {
                int direction = UnityEngine.Random.value >= 0.5f ? 1 : -1;
                forwardMove *= direction;
                upwardMove *= direction;
                positionChange *= direction;
                rotationChange *= direction;
            }

            _endPosition = _startPosition;
            _endEulerAngles = _startEulerAngles;
            if (orbitCenter != null)
                _orbitDistance = Vector3.Distance(_startPosition, orbitCenter.position);

            if (!isOrbiting && isLooping)
            {
                float timePos = 0f;
                if (forwardMove != 0f)
                {
                    _endPosition = _startPosition + _cachedTransform.forward.normalized * loopDistance;
                    timePos = Mathf.Abs(loopDistance / forwardMove);
                }
                else if (positionChange != Vector3.zero)
                {
                    _endPosition = _startPosition + positionChange.normalized * loopDistance;
                    timePos = Mathf.Abs(loopDistance / positionChange.magnitude);
                }

                float timeRot = 0f;
                if (rotationChange != Vector3.zero)
                {
                    if (loopAngle != 0f)
                    {
                        _endEulerAngles = _startEulerAngles + rotationChange.normalized * loopAngle;
                        timeRot = Mathf.Abs(loopAngle / rotationChange.magnitude);
                    }
                    else if (timePos > 0f)
                    {
                        _endEulerAngles = _startEulerAngles + rotationChange * timePos;
                    }
                }

                // Use whichever finishes first
                if (timePos > 0f)
                    _loopDuration = timePos;
                else 
                    _loopDuration = timeRot;
                _startTime = Time.time;

                isLooping = _loopDuration > 0f;
            }
        }

        private void Update()
        {
            if (isOrbiting)
            {
                if (orbitCenter == null)
                    return;
                Vector3 orbitCenterPos = orbitCenter.position;
                Vector3 targetPos = _cachedTransform.position + _cachedTransform.right * orbitSpeed * Time.deltaTime;
                targetPos.y = _startPosition.y;
                _cachedTransform.position = targetPos;
                _cachedTransform.LookAt(orbitCenter);

                targetPos = orbitCenterPos - _cachedTransform.forward * _orbitDistance;
                targetPos.y = _startPosition.y;
                _cachedTransform.position = targetPos;

                Vector3 newEulerAngles = _cachedTransform.rotation.eulerAngles;
                newEulerAngles.x = _startEulerAngles.x;
                _cachedTransform.rotation = Quaternion.Euler(newEulerAngles);
                return;
            }

            Vector3 newPos;
            if (isLooping)
            {
                Vector3 newAngles;
                bool loopReversed = loopChangeDirection && ((Time.time / _loopDuration) % 2f > 1f);
                float t = (Time.time % _loopDuration) / _loopDuration;
                if (loopReversed)
                {
                    newPos = Vector3.Lerp(_endPosition, _startPosition, t);
                    newAngles = Vector3.Lerp(_endEulerAngles, _startEulerAngles, t);
                }
                else
                {
                    newPos = Vector3.Lerp(_startPosition, _endPosition, t);
                    newAngles = Vector3.Lerp(_startEulerAngles, _endEulerAngles, t);
                }
                _cachedTransform.position = newPos;
                _cachedTransform.rotation = Quaternion.Euler(newAngles);
                return;
            }

            newPos = _cachedTransform.position;
            if (forwardMove != 0)
                newPos += _cachedTransform.forward * forwardMove * Time.deltaTime;

            if (upwardMove != 0)
                newPos += _cachedTransform.up * upwardMove * Time.deltaTime;
            
            newPos += positionChange * Time.deltaTime;

            if (isRayCasting)
            {
                if (Physics.Raycast(newPos, Vector3.down, out RaycastHit hit, rayCastMaxDistance, 1 << rayCastLayer))
                    newPos.y = hit.point.y + rayCastHeight;
            }
            _cachedTransform.position = newPos;

            Vector3 eulerAngles = _cachedTransform.rotation.eulerAngles + rotationChange * Time.deltaTime;
            _cachedTransform.rotation = Quaternion.Euler(eulerAngles);
        }
    }
}
