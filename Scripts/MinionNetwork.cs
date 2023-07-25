using UnityEngine;
using Unity.Netcode;
//TERRIBLE!!! why? because not checking for sufficient difference before writing ?
public class MinionNetwork : NetworkBehaviour
{
    private NetworkVariable<MinionNetworkData> _netState;
    private Vector3 _vel;
    private float _rotVel;
    private float _interpolateTime = 0.1f; 
    private void Awake()
    {
        var permission = NetworkVariableWritePermission.Owner;
        _netState = new NetworkVariable<MinionNetworkData>(writePerm: permission);
    }
    private void Update()
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
    private void WriteData()
    {
        var state = new MinionNetworkData()
        {
            //Position = transform.position,
            Rotation = transform.rotation.eulerAngles
        };
        if (IsServer)
        {
            _netState.Value = state;
        }
        else
        {
            WriteDataServerRPC(state);
        }
    }
    private void ReadData() //basic interpolation
    {
        //transform.position = Vector3.SmoothDamp(transform.position, _netState.Value.Position, ref _vel, _interpolateTime);
        transform.rotation = Quaternion.Euler(
            0,
            Mathf.SmoothDampAngle(transform.rotation.eulerAngles.y, _netState.Value.Rotation.y, ref _rotVel, _interpolateTime),
            0);

        //transform.position = _netState.Value.Position;
        //transform.rotation = Quaternion.Euler(0, _netState.Value.Rotation.y, 0);
    }

    [ServerRpc]
    private void WriteDataServerRPC(MinionNetworkData state)
    {
        _netState.Value = state;
    }

    struct MinionNetworkData : INetworkSerializable
    {
        //private float _x, _z;
        private short _yRot;
/*
        internal Vector3 Position
        {
            get => new Vector3(_x, 0, _z);
            set
            {
                _x = value.x;
                _z = value.z;
            }
        }*/
        internal Vector3 Rotation
        {
            get => new Vector3(0, _yRot, 0);
            set => _yRot = (short)value.y;
        }
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            //serializer.SerializeValue(ref _x);
            //serializer.SerializeValue(ref _z);
            serializer.SerializeValue(ref _yRot);
        }
    }
}
