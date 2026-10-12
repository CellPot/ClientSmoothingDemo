namespace Techniques
{
    /// <summary>
    /// Constant-velocity Kalman filter for one axis. The state is only ever advanced to snapshot timestamps;
    /// rendering extrapolates from it with <see cref="PositionAt"/> and never mutates it.
    /// </summary>
    public class KalmanAxis
    {
        private float _position;
        private float _velocity;
        private float _pPos = 1f; // position error covariance
        private float _pVel = 1f; // velocity error covariance

        public float StateTime { get; private set; }

        public void Init(float pos, float vel, float time)
        {
            _position = pos;
            _velocity = vel;
            _pPos = 1f;
            _pVel = 1f;
            StateTime = time;
        }

        /// <summary>Predict to the snapshot's timestamp, then correct with its position and velocity.</summary>
        public void Update(float measPos, float measVel, float time, float Q, float R)
        {
            float dt = time - StateTime;
            if (dt < 0f) dt = 0f; // reordered/duplicate timestamp: correct without advancing
            StateTime = time > StateTime ? time : StateTime;

            _position += _velocity * dt;
            _pPos += _pVel * dt * dt + Q * dt;
            _pVel += Q * dt;

            float kp = _pPos / (_pPos + R);
            _position += kp * (measPos - _position);
            _pPos *= 1f - kp;

            // Velocity is a noisier derivative, so trust it less than position.
            float kv = _pVel / (_pVel + R * 4f);
            _velocity += kv * (measVel - _velocity);
            _pVel *= 1f - kv;
        }

        /// <summary>Estimated position at <paramref name="time"/> (extrapolated from the last snapshot).</summary>
        public float PositionAt(float time) => _position + _velocity * (time - StateTime);
    }
}
