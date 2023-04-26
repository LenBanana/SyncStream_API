using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace SyncStreamAPI.Helper
{
    public class NAudioTools
    {
        public static byte[] GetWaveform(string inputFilePath)
        {
            using (var reader = new AudioFileReader(inputFilePath))
            {
                var sampleProvider = reader.ToSampleProvider();
                var samplesPerSecond = (int)sampleProvider.WaveFormat.SampleRate;
                var channelCount = sampleProvider.WaveFormat.Channels;
                var durationInSeconds = reader.TotalTime.TotalSeconds;
                var samplesPerFrame = samplesPerSecond / 30;
                var buffer = new float[samplesPerFrame * channelCount];

                using (var memoryStream = new MemoryStream())
                {
                    using (var writer = new WaveFileWriter(memoryStream, new WaveFormat(samplesPerSecond, 16, channelCount)))
                    {
                        while (reader.Position < reader.Length)
                        {
                            var readSamples = sampleProvider.Read(buffer, 0, buffer.Length);
                            var waveform = new float[readSamples / channelCount];
                            for (int i = 0; i < readSamples; i += channelCount)
                            {
                                var sampleValue = buffer[i];
                                waveform[i / channelCount] = sampleValue;
                            }
                            writer.WriteSamples(waveform, 0, waveform.Length);
                        }
                    }
                    return memoryStream.ToArray();
                }
            }
        }
    }
}
