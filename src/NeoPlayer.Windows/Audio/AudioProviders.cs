using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
namespace NeoPlayer.Windows.Audio;
public sealed class GateSampleProvider(ISampleProvider source) : ISampleProvider
{
 public WaveFormat WaveFormat=>source.WaveFormat; public bool Enabled{get;set;}
 public int Read(float[] b,int o,int c){if(!Enabled){Array.Clear(b,o,c);return c;}var n=source.Read(b,o,c);if(n<c)Array.Clear(b,o+n,c-n);return c;}
}
public sealed class DspSampleProvider : ISampleProvider
{
 readonly ISampleProvider source; readonly int ch; readonly object gate=new(); BiQuadFilter[][] eq; float[] gains=new float[10]; float bass,virtualizer,loudnessDb,autoGainDb; readonly float[] freqs={31,62,125,250,500,1000,2000,4000,8000,16000};
 public WaveFormat WaveFormat=>source.WaveFormat; public event Action<float[]>? Samples;
 public DspSampleProvider(ISampleProvider src){source=src;ch=src.WaveFormat.Channels;eq=Enumerable.Range(0,ch).Select(_=>new BiQuadFilter[10]).ToArray();Rebuild();}
 public void Configure(float[] bandDb,float b,float virt,float loudDb,float normalizationDb){lock(gate){gains=bandDb.Concat(Enumerable.Repeat(0f,10)).Take(10).ToArray();bass=b;virtualizer=virt;loudnessDb=loudDb;autoGainDb=normalizationDb;Rebuild();}}
 void Rebuild(){for(int c=0;c<ch;c++)for(int i=0;i<10;i++){var gain=gains[i]+(i<3?bass*3f:0);eq[c][i]=BiQuadFilter.PeakingEQ(WaveFormat.SampleRate,freqs[i],1f,Math.Clamp(gain,-12,12));}}
 public int Read(float[] b,int o,int count){int n=source.Read(b,o,count);lock(gate){float gain=(float)Math.Pow(10,(loudnessDb+autoGainDb)/20.0);for(int i=0;i<n;i++){int c=i%ch;float x=b[o+i];for(int k=0;k<10;k++)x=eq[c][k].Transform(x);b[o+i]=Math.Clamp(x*gain,-1f,1f);}if(ch==2&&virtualizer>0){float w=Math.Clamp(virtualizer,0,1)*.22f;for(int i=0;i+1<n;i+=2){float l=b[o+i],rr=b[o+i+1];b[o+i]=Math.Clamp(l+(l-rr)*w,-1,1);b[o+i+1]=Math.Clamp(rr+(rr-l)*w,-1,1);}}}
 if(n>0){var sample=new float[Math.Min(2048,n)];Array.Copy(b,o,sample,0,sample.Length);Samples?.Invoke(sample);}return n;}
}
public sealed class Deck : IDisposable
{
 public FfmpegPcmSource Decoder{get;} public DspSampleProvider Dsp{get;} public VolumeSampleProvider Volume{get;} public GateSampleProvider Gate{get;} public long SongId{get;} public bool Enabled{get=>Gate.Enabled;set=>Gate.Enabled=value;}
 public Deck(long id,string path,double start,float speed){SongId=id;Decoder=new(path,start,speed);Dsp=new(Decoder);Volume=new VolumeSampleProvider(Dsp){Volume=1};Gate=new GateSampleProvider(Volume);}
 public void Dispose()=>Decoder.Dispose();
}