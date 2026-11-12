using System.Buffers.Binary;
namespace Aholo3D.Core;

public static class JpegFrameSize
{
    /// <summary>Check the encoded dimensions before allocating a platform bitmap.</summary>
    public static (int Width,int Height) Validate(ReadOnlySpan<byte> bytes,long maximumPixels)
    {
        if(maximumPixels<=0)throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        if(bytes.Length<4||bytes[0]!=255||bytes[1]!=216)throw new InvalidDataException("Invalid cloud JPEG header.");
        int position=2;
        while(position<bytes.Length)
        {
            if(bytes[position++]!=255)throw new InvalidDataException("Invalid cloud JPEG marker.");
            while(position<bytes.Length&&bytes[position]==255)position++;
            if(position>=bytes.Length)break;byte marker=bytes[position++];
            if(marker is 0xd9 or 0xda)break;
            if(marker==1||marker is >=0xd0 and <=0xd7)continue;
            if(position+2>bytes.Length)break;int length=BinaryPrimitives.ReadUInt16BigEndian(bytes[position..]);
            if(length<2||length>bytes.Length-position)throw new InvalidDataException("Truncated cloud JPEG header.");
            if(marker is 0xc0 or 0xc1 or 0xc2)
            {
                if(length<8)break;int height=BinaryPrimitives.ReadUInt16BigEndian(bytes[(position+3)..]),width=BinaryPrimitives.ReadUInt16BigEndian(bytes[(position+5)..]);
                if(width==0||height==0||(long)width*height>maximumPixels)throw new InvalidDataException("Cloud image dimensions exceed the preview decoding capacity.");
                return(width,height);
            }
            position+=length;
        }
        throw new InvalidDataException("Unsupported cloud JPEG frame header.");
    }
}
