using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using Libplanet.Crypto;
using Libplanet.Net.Messages;
using NetMQ;
using Xunit;

namespace Libplanet.Tests.Net.Messages
{
    public class RecentStatesTest
    {
        [Fact]
        public void Constructor()
        {
            var emptyBlockStates = ImmutableDictionary<
                HashDigest<SHA256>,
                IImmutableDictionary<Address, object>
            >.Empty;
            Assert.Throws<ArgumentNullException>(() =>
                new RecentStates(
                    default,
                    null,
                    ImmutableDictionary<Address, IImmutableList<HashDigest<SHA256>>>.Empty
                )
            );
            Assert.Throws<ArgumentNullException>(() =>
                new RecentStates(
                    default,
                    emptyBlockStates,
                    null
                )
            );
        }

        [Fact]
        public void DataFrames()
        {
            // This test lengthens long... Please read the brief description of the entire payload
            // structure from the comment in the RecentStates.DataFrames property code.
            ISet<Address> accounts = Enumerable.Repeat(0, 5).Select(_ =>
                new PrivateKey().PublicKey.ToAddress()
            ).ToHashSet();
            int accountsCount = accounts.Count;
            var privKey = new PrivateKey();

            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            var randomBytesBuffer = new byte[HashDigest<SHA256>.Size];
            (HashDigest<SHA256>, IImmutableDictionary<Address, object>)[] blockStates =
                accounts.SelectMany(address =>
                {
                    rng.GetNonZeroBytes(randomBytesBuffer);
                    var blockHash1 = new HashDigest<SHA256>(randomBytesBuffer);
                    rng.GetNonZeroBytes(randomBytesBuffer);
                    var blockHash2 = new HashDigest<SHA256>(randomBytesBuffer);
                    IImmutableDictionary<Address, object> emptyState =
                        ImmutableDictionary<Address, object>.Empty;
                    return new[]
                    {
                        (blockHash1, emptyState.Add(address, $"A:{blockHash1}:{address}")),
                        (blockHash2, emptyState.Add(address, $"B:{blockHash2}:{address}")),
                    };
                }).ToArray();
            IImmutableDictionary<HashDigest<SHA256>, IImmutableDictionary<Address, object>>
                compressedBlockStates = blockStates.Where(
                    (_, i) => i % 2 == 1
                ).ToImmutableDictionary(p => p.Item1, p => p.Item2);
            HashDigest<SHA256> blockHash = blockStates.Last().Item1;

            IImmutableDictionary<Address, IImmutableList<HashDigest<SHA256>>> stateRefs =
                accounts.Select(a =>
                {
                    var states = blockStates
                        .Where(pair => pair.Item2.ContainsKey(a))
                        .Select(pair => pair.Item1)
                        .ToImmutableList();
                    return new KeyValuePair<Address, IImmutableList<HashDigest<SHA256>>>(a, states);
                }).ToImmutableDictionary();

            RecentStates reply = new RecentStates(
                blockHash,
                compressedBlockStates,
                stateRefs
            );
            NetMQMessage msg = reply.ToNetMQMessage(privKey);
            const int headerSize = 3;  // type, pubkey, sig
            int stateRefsOffset = headerSize + 1;
            int blockStatesOffset = stateRefsOffset + 1 + (accountsCount * 4);
            Assert.Equal(
               blockStatesOffset + 1 + (compressedBlockStates.Count * 4),
               msg.FrameCount
            );
            Assert.Equal(blockHash, new HashDigest<SHA256>(msg[headerSize].Buffer));
            Assert.Equal(accountsCount, msg[headerSize + 1].ConvertToInt32());
            for (int i = 0; i < accountsCount; i++)
            {
                int offset = stateRefsOffset + 1 + (i * 4);
                Assert.Equal(Address.Size, msg[offset].BufferSize);
                Address address = new Address(msg[offset].Buffer);
                Assert.Contains(address, accounts);

                Assert.Equal(4, msg[offset + 1].BufferSize);
                Assert.Equal(2, msg[offset + 1].ConvertToInt32());

                Assert.Equal(HashDigest<SHA256>.Size, msg[offset + 2].BufferSize);
                Assert.Equal(stateRefs[address][0], new HashDigest<SHA256>(msg[offset + 2].Buffer));
                Assert.Equal(HashDigest<SHA256>.Size, msg[offset + 3].BufferSize);
                Assert.Equal(stateRefs[address][1], new HashDigest<SHA256>(msg[offset + 3].Buffer));

                accounts.Remove(address);
            }

            Assert.Empty(accounts);
            Assert.Equal(compressedBlockStates.Count, msg[blockStatesOffset].ConvertToInt32());

            var formatter = new BinaryFormatter();
            for (int i = 0; i < compressedBlockStates.Count; i++)
            {
                int offset = blockStatesOffset + 1 + (i * 4);

                var hash = new HashDigest<SHA256>(msg[offset].Buffer);
                Assert.Contains(hash, compressedBlockStates);
                Assert.Equal(1, msg[offset + 1].ConvertToInt32());

                var addr = new Address(msg[offset + 2].Buffer);
                Assert.Equal(compressedBlockStates[hash].Keys.First(), addr);

                using (var stream = new MemoryStream())
                {
                    stream.Write(msg[offset + 3].Buffer, 0, msg[offset + 3].BufferSize);
                    stream.Seek(0, SeekOrigin.Begin);
                    string state = (string)formatter.Deserialize(stream);
                    Assert.Equal($"B:{hash}:{addr}", state);
                }
            }

            msg = reply.ToNetMQMessage(privKey);
            var parsed = new RecentStates(msg.Skip(headerSize).ToArray());
            Assert.Equal(blockHash, parsed.BlockHash);
            Assert.False(parsed.Missing);
            Assert.Equal(compressedBlockStates, parsed.BlockStates);
            Assert.Equal(stateRefs, parsed.StateReferences);

            RecentStates missing = new RecentStates(blockHash, null, null);
            msg = missing.ToNetMQMessage(privKey);
            Assert.Equal(blockHash, new HashDigest<SHA256>(msg[headerSize].Buffer));
            Assert.Equal(-1, msg[headerSize + 1].ConvertToInt32());

            parsed = new RecentStates(missing.ToNetMQMessage(privKey).Skip(headerSize).ToArray());
            Assert.Equal(blockHash, parsed.BlockHash);
            Assert.True(parsed.Missing);
        }
    }
}
