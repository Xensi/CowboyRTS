using UnityEngine;
using Unity.Netcode; 
public class MinionNetwork : NetworkBehaviour
{
    [SerializeField] private float rotationDifferenceThreshold = 22.5f;
    [SerializeField] private float positionDifferenceThreshold = .1f;
    private NetworkVariable<RotationData> _netRot;
    private NetworkVariable<PositionData> _netPos;
    private NetworkVariable<MinionData> _netData;
    private Vector3 oldRotation;
    private Vector3 oldPosition;
    private Vector3 _vel;
    private float _rotVel;
    [SerializeField] private float _interpolateTime = 0.1f;
    [SerializeField] private float _rotInterpolate = 0.1f;
    [SerializeField] private bool _useServerAuthoritative = true;
    [SerializeField] private bool _combinePackets = true; //combining packets seems better!
    private void Awake()
    { 
        var perm = _useServerAuthoritative ? NetworkVariableWritePermission.Server : NetworkVariableWritePermission.Owner;

        _netRot = new NetworkVariable<RotationData>(writePerm : perm); //client authoratative
        _netPos = new NetworkVariable<PositionData>(writePerm: perm); //client authoratative
        _netData = new NetworkVariable<MinionData>(writePerm: perm); //client authoratative
    }
    public override void OnNetworkSpawn()
    {
        TransmitState();
        oldPosition = transform.position;
        oldRotation = transform.rotation.eulerAngles;
    }
    private float CalculateChangeInRotation()
    {
        float diff = Mathf.Abs(transform.rotation.eulerAngles.y - oldRotation.y);
        return diff;
    }
    private float CalculateChangeInPosition()
    {
        float diff = Vector3.Distance(transform.position, oldPosition);
        return diff;
    }
    private void FixedUpdate()
    {
        if (IsOwner)
        {
            TransmitState();
        }
        else //if not owner, then read w interpolation
        { 
            ReadData();
        }
    }
    private void TransmitState()
    {
        //get state if there is a change
        if (_combinePackets)
        {
            float diff = CalculateChangeInRotation();
            float posDiff = CalculateChangeInPosition();
            if (diff >= rotationDifferenceThreshold || posDiff >= positionDifferenceThreshold)
            {
                MinionData data = WriteData();
                //Debug.Log("updating data");
                if (IsServer || !_useServerAuthoritative) //write if you are server, or if using owner-auth
                {
                    _netData.Value = data;
                }
                else
                {
                    UpdateNetDataServerRpc(data);
                }
            } 
        }
        else
        {
            float diff = CalculateChangeInRotation();
            if (diff >= rotationDifferenceThreshold)
            {
                RotationData data = WriteRotation();
                //Debug.Log("updating rot");
                if (IsServer || !_useServerAuthoritative) //write if you are server, or if using owner-auth
                {
                    _netRot.Value = data;
                }
                else
                {
                    UpdateNetRotServerRpc(data);
                }
            }
            float posDiff = CalculateChangeInPosition();
            if (posDiff >= positionDifferenceThreshold)
            {
                PositionData data = WritePosition();
                //Debug.Log("updating pos");
                if (IsServer || !_useServerAuthoritative) //write if you are server, or if using owner-auth
                {
                    _netPos.Value = data;
                }
                else
                {
                    UpdateNetPosServerRpc(data);
                }
            }
        } 
    }
    [ServerRpc(RequireOwnership = false)]
    private void UpdateNetRotServerRpc(RotationData data)
    { 
        _netRot.Value = data;
    }
    [ServerRpc(RequireOwnership = false)]
    private void UpdateNetPosServerRpc(PositionData data)
    { 
        _netPos.Value = data;
    }
    private PositionData WritePosition() //update network variable
    {
        var state = new PositionData()
        {
            Position = transform.position,
        };
        //_netPos.Value = state;
        oldPosition = transform.position; //update this
        return state;
    }
    private RotationData WriteRotation() //update network variable
    {
        var state = new RotationData()
        {
            Rotation = transform.rotation.eulerAngles
        };
        //_netRot.Value = state;  
        oldRotation = transform.rotation.eulerAngles; //update this
        return state;
    }
    [ServerRpc(RequireOwnership = false)] 
    private void UpdateNetDataServerRpc(MinionData data)
    { 
        _netData.Value = data;
    }
    private MinionData WriteData()
    {
        var state = new MinionData
        {
            Rotation = transform.rotation.eulerAngles,
            Position = transform.position
        };
        oldRotation = transform.rotation.eulerAngles; 
        oldPosition = transform.position;
        return state;
    }
    private void ReadData() //locally update to "match" actual object
    { 
        if (_combinePackets)
        {
            transform.SetPositionAndRotation(Vector3.SmoothDamp(transform.position, _netData.Value.Position, ref _vel, _interpolateTime), Quaternion.Euler(
                0,
                Mathf.SmoothDampAngle(transform.rotation.eulerAngles.y, _netData.Value.Rotation.y, ref _rotVel, _rotInterpolate),
                0)); 
        }
        else
        {
            transform.SetPositionAndRotation(Vector3.SmoothDamp(transform.position, _netPos.Value.Position, ref _vel, _interpolateTime), Quaternion.Euler(
                0,
                Mathf.SmoothDampAngle(transform.rotation.eulerAngles.y, _netRot.Value.Rotation.y, ref _rotVel, _rotInterpolate),
                0));
        }

        //transform.position = _netState.Value.Position;
        //transform.rotation = Quaternion.Euler(0, _netState.Value.Rotation.y, 0);
    }
    struct MinionData : INetworkSerializable
    {
        private short _yRot;
        private ushort _x, _z;
        internal Vector3 Rotation
        {
            get => new(0, _yRot, 0);
            set => _yRot = (short)value.y;
        }
        internal Vector3 Position
        {
            get => new(Mathf.HalfToFloat(_x), 0, Mathf.HalfToFloat(_z));
            set
            {
                //floats input are converted to ushorts
                _x = Mathf.FloatToHalf(value.x);
                _z = Mathf.FloatToHalf(value.z);
            }
        }
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _yRot);
            serializer.SerializeValue(ref _x);
            serializer.SerializeValue(ref _z);
        } 
    }
    struct RotationData : INetworkSerializable
    { 
        private short _yRot; 
        internal Vector3 Rotation
        {
            get => new(0, _yRot, 0);
            set => _yRot = (short)value.y;
        }
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        { 
            serializer.SerializeValue(ref _yRot);
        }
    }

    struct PositionData : INetworkSerializable
    {
        private ushort _x, _z; 

        internal Vector3 Position
        {
            get => new(Mathf.HalfToFloat(_x), 0, Mathf.HalfToFloat(_z));
            set
            {
                //floats input are converted to ushorts
                _x = Mathf.FloatToHalf(value.x);
                _z = Mathf.FloatToHalf(value.z);
            }
        } 
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _x);
            serializer.SerializeValue(ref _z); 
        }
    }
}
