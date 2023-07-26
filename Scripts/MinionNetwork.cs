using UnityEngine;
using Unity.Netcode; 
public class MinionNetwork : NetworkBehaviour
{
    [SerializeField] private float rotationDifferenceThreshold = 22.5f;
    [SerializeField] private float positionDifferenceThreshold = .1f;
    private NetworkVariable<RotationData> _netRot;
    private NetworkVariable<PositionData> _netPos;
    private Vector3 oldRotation;
    private Vector3 oldPosition;
    private Vector3 _vel;
    private float _rotVel;
    [SerializeField] private float _interpolateTime = 0.1f;
    [SerializeField] private float _rotInterpolate = 0.1f;
    private void Awake()
    { 
        _netRot = new NetworkVariable<RotationData>(writePerm : NetworkVariableWritePermission.Owner); //client authoratative
        _netPos = new NetworkVariable<PositionData>(writePerm: NetworkVariableWritePermission.Owner); //client authoratative
    }
    public override void OnNetworkSpawn()
    {
        WriteData();
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
    private void WriteData()
    { 
        float diff = CalculateChangeInRotation();
        if (diff >= rotationDifferenceThreshold)
        {
            //Debug.Log("writing rot");
            WriteRotation();
        }
        float posDiff = CalculateChangeInPosition();
        if (posDiff >= positionDifferenceThreshold)
        {
            //Debug.Log("writing pos");
            WritePosition();
        }
    }
    private void FixedUpdate()
    {
        if (IsOwner) //write
        {
            WriteData();
        }
        else //read w interpolation
        {
            ReadData();
        }
    }
    private void WritePosition() //update network variable
    {
        var state = new PositionData()
        {
            Position = transform.position, 
        };
        _netPos.Value = state;
        oldPosition = transform.position; //update this
    }
    private void WriteRotation() //update network variable
    {
        var state = new RotationData()
        { 
            Rotation = transform.rotation.eulerAngles
        };
        _netRot.Value = state;  
        oldRotation = transform.rotation.eulerAngles; //update this
    }  
    private void ReadData() //locally update to "match" actual object
    { 
        transform.position = Vector3.SmoothDamp(transform.position, _netPos.Value.Position, ref _vel, _interpolateTime);
        transform.rotation = Quaternion.Euler(
            0,
            Mathf.SmoothDampAngle(transform.rotation.eulerAngles.y, _netRot.Value.Rotation.y, ref _rotVel, _rotInterpolate),
            0);

        //transform.position = _netState.Value.Position;
        //transform.rotation = Quaternion.Euler(0, _netState.Value.Rotation.y, 0);
    }

    struct RotationData : INetworkSerializable
    { 
        private short _yRot; 
        internal Vector3 Rotation
        {
            get => new Vector3(0, _yRot, 0);
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
            get => new Vector3(Mathf.HalfToFloat(_x), 0, Mathf.HalfToFloat(_z));
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
