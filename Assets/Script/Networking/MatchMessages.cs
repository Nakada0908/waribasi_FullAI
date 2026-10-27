using System;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Networking
{
    public struct MatchRequestMessage : INetworkSerializable
    {
        public FixedString64Bytes MatchId;
        public ulong RequestId;
        public long ExpectedVersion;
        public int Operation;
        public int Source;
        public int Target;
        public int Left;
        public int Right;
        public long CardId;
        public bool SwapValues;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref MatchId);
            serializer.SerializeValue(ref RequestId);
            serializer.SerializeValue(ref ExpectedVersion);
            serializer.SerializeValue(ref Operation);
            serializer.SerializeValue(ref Source);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref Left);
            serializer.SerializeValue(ref Right);
            serializer.SerializeValue(ref CardId);
            serializer.SerializeValue(ref SwapValues);
        }

        public bool TryDecode(out MatchRequest request)
        {
            request = default;
            if (!Guid.TryParseExact(MatchId.ToString(), "N", out var match))
            {
                return false;
            }
            request = new MatchRequest(match, RequestId, ExpectedVersion, (MatchOperation)Operation,
                Source, Target, Left, Right, CardId, SwapValues);
            return true;
        }
    }

}
