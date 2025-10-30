namespace MareSynchronos.Utils;

public static class PngHdr
{
	private static readonly byte[] _magicSignature = [137, 80, 78, 71, 13, 10, 26, 10];
	private static readonly byte[] _IHDR = [(byte)'I', (byte)'H', (byte)'D', (byte)'R'];
	public static readonly (int Width, int Height) InvalidSize = (0, 0);

	public static (int Width, int Height) TryExtractDimensions(Stream stream)
	{
		Span<byte> buffer = stackalloc byte[8];

		try
		{
			stream.ReadExactly(buffer[..8]);

			// All PNG files start with the same 8 bytes
			if (!buffer.SequenceEqual(_magicSignature))
				return InvalidSize;

			stream.ReadExactly(buffer[..8]);

			uint ihdrLength = BitConverter.ToUInt32(buffer);

			// The next four bytes will be the length of the IHDR section (it should be 13 bytes but we only need 8)
			if (ihdrLength < 8)
				return InvalidSize;

			// followed by ASCII "IHDR"
			if (!buffer[4..].SequenceEqual(_IHDR))
				return InvalidSize;

			stream.ReadExactly(buffer[..8]);

			uint width = BitConverter.ToUInt32(buffer);
			uint height = BitConverter.ToUInt32(buffer[4..]);

			// Validate the width/height are non-negative and... that's all we care about!
			if (width > int.MaxValue || height > int.MaxValue)
				return InvalidSize;

			return ((int)width, (int)height);
		}
		catch (EndOfStreamException)
		{
			return InvalidSize;
		}
	}
}
