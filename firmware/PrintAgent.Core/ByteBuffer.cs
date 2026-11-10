using System;

namespace PrintAgent.Core
{
    // Растущий буфер байт (замена List<byte>, которого нет в nanoFramework)
    public class ByteBuffer
    {
        private byte[] data = new byte[256];
        private int count;

        public void Append(byte value)
        {
            if (this.count == this.data.Length)
            {
                byte[] bigger = new byte[this.data.Length * 2];
                Array.Copy(this.data, bigger, this.count);
                this.data = bigger;
            }
            this.data[this.count] = value;
            this.count++;
        }

        public void Append(byte[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                this.Append(values[i]);
            }
        }

        // Символы строки как байты; строка должна быть уже ASCII (см. EscPosBuilder.Sanitize)
        public void Append(string ascii)
        {
            for (int i = 0; i < ascii.Length; i++)
            {
                this.Append((byte)ascii[i]);
            }
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[this.count];
            Array.Copy(this.data, result, this.count);
            return result;
        }
    }
}
