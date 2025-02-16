﻿using OozSharp;

namespace Unreal.Encryption
{
    /// <summary>  
    /// see https://github.com/EpicGames/UnrealEngine/blob/release/Engine/Plugins/Runtime/PacketHandlers/CompressionComponents/Oodle/Source/OodleHandlerComponent/Private/OodleUtils.cpp 
    /// </summary>
    public static class Oodle
    {
        static readonly Kraken kraken = new Kraken();

        /// <summary>
        /// see https://github.com/EpicGames/UnrealEngine/blob/70bc980c6361d9a7d23f6d23ffe322a2d6ef16fb/Engine/Plugins/Runtime/PacketHandlers/CompressionComponents/Oodle/Source/OodleHandlerComponent/Private/OodleUtils.cpp#L14
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="uncompressedSize"></param>
        /// <returns>byte[]</returns>
        public static byte[] DecompressReplayData(byte[] buffer, int uncompressedSize)
        {
            return kraken.Decompress(buffer, uncompressedSize);
        }
    }
}
