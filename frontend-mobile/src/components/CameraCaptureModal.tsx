import { useEffect, useRef } from 'react';
import { X, Camera as CameraIcon } from 'lucide-react';

/** Full-screen live camera preview with a shutter button, used instead of a plain
 * <input capture> because desktop browsers (Windows/Chrome/Edge) mostly ignore the
 * `capture` attribute and just show the regular file picker instead of the webcam.
 * getUserMedia works consistently across desktop and mobile browsers. */
export function CameraCaptureModal({
  onCapture,
  onClose,
  onUnavailable,
}: {
  onCapture: (file: File) => void;
  onClose: () => void;
  onUnavailable: () => void;
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);

  useEffect(() => {
    let cancelled = false;

    const start = async () => {
      try {
        let stream: MediaStream;
        try {
          stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
        } catch {
          // Most laptops/desktops have no rear/environment camera; fall back to the default one.
          stream = await navigator.mediaDevices.getUserMedia({ video: true });
        }
        if (cancelled) {
          stream.getTracks().forEach((track) => track.stop());
          return;
        }
        streamRef.current = stream;
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
        }
      } catch {
        if (!cancelled) onUnavailable();
      }
    };

    void start();

    return () => {
      cancelled = true;
      streamRef.current?.getTracks().forEach((track) => track.stop());
    };
  }, [onUnavailable]);

  const handleCapture = () => {
    const video = videoRef.current;
    if (!video || video.videoWidth === 0) return;
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.drawImage(video, 0, 0);
    canvas.toBlob(
      (blob) => {
        if (!blob) return;
        onCapture(new File([blob], `etiqueta-${Date.now()}.jpg`, { type: 'image/jpeg' }));
      },
      'image/jpeg',
      0.92,
    );
  };

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-black">
      <div className="flex items-center justify-between p-3">
        <span className="text-sm font-medium text-white">Escanear etiqueta</span>
        <button
          type="button"
          onClick={onClose}
          aria-label="Cerrar"
          className="rounded-full p-1.5 text-white hover:bg-white/10"
        >
          <X className="h-5 w-5" />
        </button>
      </div>
      <div className="relative flex-1 overflow-hidden">
        <video ref={videoRef} autoPlay playsInline muted className="h-full w-full object-cover" />
      </div>
      <div className="flex items-center justify-center p-6 pb-[max(1.5rem,env(safe-area-inset-bottom))]">
        <button
          type="button"
          onClick={handleCapture}
          aria-label="Capturar foto"
          className="flex h-16 w-16 items-center justify-center rounded-full border-4 border-white/70 bg-white/20"
        >
          <CameraIcon className="h-6 w-6 text-white" />
        </button>
      </div>
    </div>
  );
}
