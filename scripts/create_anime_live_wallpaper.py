import os
import sys
import math
import random
import subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

def generate_live_wallpaper(input_image_path, output_mp4_path, duration=8.0, fps=60):
    print(f"🎬 Loading image: {input_image_path}")
    src_img = Image.open(input_image_path).convert("RGBA")
    
    # Target resolution: 1080p (1920x1080)
    target_w, target_h = 1920, 1080
    
    # High-quality upscale base canvas slightly oversized for camera pan/zoom
    overscan = 1.08
    canvas_w = int(target_w * overscan)
    canvas_h = int(target_h * overscan)
    
    base_resized = src_img.resize((canvas_w, canvas_h), Image.Resampling.LANCZOS)
    base_np = np.array(base_resized).astype(np.float32)
    
    total_frames = int(duration * fps)
    print(f"✨ Generating {total_frames} frames ({duration}s @ {fps}fps) at {target_w}x{target_h}...")
    
    # Setup particles with periodic harmonics for mathematically seamless loop
    np.random.seed(42)
    num_particles = 65
    particles = []
    for _ in range(num_particles):
        x0 = np.random.uniform(50, target_w - 50)
        y0 = np.random.uniform(50, target_h - 50)
        radius = np.random.uniform(1.8, 5.5)
        # Integer harmonics ensure f(0) == f(T)
        k_x = np.random.choice([1, 2, 3])
        k_y = np.random.choice([1, 2])
        phi_x = np.random.uniform(0, 2 * math.pi)
        phi_y = np.random.uniform(0, 2 * math.pi)
        amp_x = np.random.uniform(25, 70)
        amp_y = np.random.uniform(15, 45)
        base_alpha = np.random.uniform(0.25, 0.75)
        glow_color = random.choice([(255, 255, 255), (255, 235, 210), (220, 230, 255)])
        particles.append({
            'x0': x0, 'y0': y0, 'r': radius,
            'k_x': k_x, 'k_y': k_y,
            'phi_x': phi_x, 'phi_y': phi_y,
            'amp_x': amp_x, 'amp_y': amp_y,
            'alpha': base_alpha,
            'color': glow_color
        })

    # Start ffmpeg process pipe
    ffmpeg_cmd = [
        "/opt/homebrew/bin/ffmpeg", "-y",
        "-f", "rawvideo",
        "-vcodec", "rawvideo",
        "-s", f"{target_w}x{target_h}",
        "-pix_fmt", "rgb24",
        "-r", str(fps),
        "-i", "-",
        "-c:v", "libx264",
        "-preset", "slow",
        "-crf", "17",
        "-pix_fmt", "yuv420p",
        output_mp4_path
    ]
    
    proc = subprocess.Popen(ffmpeg_cmd, stdin=subprocess.PIPE, stderr=subprocess.PIPE)
    
    # Pre-generate particle glow brushes
    brush_cache = {}
    for r_idx in range(15):
        rad = 1.5 + r_idx * 0.4
        brush_size = int(rad * 5)
        b_img = Image.new("RGBA", (brush_size * 2, brush_size * 2), (0, 0, 0, 0))
        b_draw = ImageDraw.Draw(b_img)
        # Soft outer glow
        b_draw.ellipse([brush_size - rad * 2, brush_size - rad * 2, brush_size + rad * 2, brush_size + rad * 2], fill=(255, 255, 255, 60))
        b_draw.ellipse([brush_size - rad, brush_size - rad, brush_size + rad, brush_size + rad], fill=(255, 255, 255, 230))
        b_img = b_img.filter(ImageFilter.GaussianBlur(1.0))
        brush_cache[r_idx] = (rad, b_img, brush_size)
    
    center_x = canvas_w / 2.0
    center_y = canvas_h / 2.0
    
    for frame_idx in range(total_frames):
        t = frame_idx / total_frames  # normalized 0.0 to 1.0
        theta = 2.0 * math.pi * t
        
        # 1. Subtle Ken Burns zoom & float
        # Zoom factor smoothly oscillates between 1.00 and 1.035
        zoom = 1.0 + 0.035 * (0.5 - 0.5 * math.cos(theta))
        cur_w = target_w / zoom
        cur_h = target_h / zoom
        
        # Subtle horizontal & vertical float
        shift_x = 24.0 * math.sin(theta)
        shift_y = 12.0 * math.sin(2.0 * theta) / 2.0
        
        crop_x1 = max(0, center_x + shift_x - cur_w / 2.0)
        crop_y1 = max(0, center_y + shift_y - cur_h / 2.0)
        crop_x2 = min(canvas_w, crop_x1 + cur_w)
        crop_y2 = min(canvas_h, crop_y1 + cur_h)
        
        cropped = base_resized.crop((int(crop_x1), int(crop_y1), int(crop_x2), int(crop_y2)))
        frame_img = cropped.resize((target_w, target_h), Image.Resampling.BILINEAR)
        
        # 2. Cinematic Light Beam / Shimmer Overlay
        shimmer_img = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
        s_draw = ImageDraw.Draw(shimmer_img)
        # Moving soft sunbeam angle
        beam_x = target_w * 0.3 + 120.0 * math.sin(theta)
        beam_opacity = int(18 + 10 * math.sin(theta + 0.5))
        s_draw.polygon([
            (beam_x - 180, 0),
            (beam_x + 320, 0),
            (beam_x + 100, target_h),
            (beam_x - 380, target_h)
        ], fill=(255, 245, 220, beam_opacity))
        shimmer_img = shimmer_img.filter(ImageFilter.GaussianBlur(55))
        frame_img.alpha_composite(shimmer_img)
        
        # 3. Floating Dust & Sparkle Particles (Mathematically Seamless)
        particle_layer = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
        for p in particles:
            px = p['x0'] + p['amp_x'] * math.sin(p['k_x'] * theta + p['phi_x'])
            py = p['y0'] + p['amp_y'] * math.cos(p['k_y'] * theta + p['phi_y'])
            p_alpha = p['alpha'] * (0.7 + 0.3 * math.sin(p['k_x'] * theta + p['phi_x']))
            
            # Select brush
            r_idx = min(14, max(0, int((p['r'] - 1.5) / 0.4)))
            _, b_img, b_size = brush_cache[r_idx]
            
            # Tint and paste
            tinted_b = b_img.copy()
            tint_data = np.array(tinted_b).astype(np.float32)
            c = p['color']
            tint_data[:, :, 0] = (tint_data[:, :, 0] * (c[0] / 255.0))
            tint_data[:, :, 1] = (tint_data[:, :, 1] * (c[1] / 255.0))
            tint_data[:, :, 2] = (tint_data[:, :, 2] * (c[2] / 255.0))
            tint_data[:, :, 3] = tint_data[:, :, 3] * p_alpha
            tinted_b = Image.fromarray(np.clip(tint_data, 0, 255).astype(np.uint8))
            
            particle_layer.paste(tinted_b, (int(px - b_size), int(py - b_size)), tinted_b)
            
        frame_img.alpha_composite(particle_layer)
        
        # Convert to RGB24 bytes and write to ffmpeg stdin
        rgb_frame = frame_img.convert("RGB")
        proc.stdin.write(rgb_frame.tobytes())
        
        if (frame_idx + 1) % 60 == 0 or frame_idx == total_frames - 1:
            print(f"  --> Rendered frame {frame_idx + 1}/{total_frames} ({(frame_idx+1)/total_frames*100:.1f}%)")
            
    proc.stdin.close()
    stderr_output = proc.stderr.read()
    proc.wait()
    
    if proc.returncode == 0:
        file_size = os.path.getsize(output_mp4_path) / (1024 * 1024)
        print(f"🎉 Live Wallpaper generated successfully!")
        print(f"📍 Output File: {output_mp4_path}")
        print(f"📦 File Size: {file_size:.2f} MB")
    else:
        print(f"❌ FFmpeg error: {stderr_output.decode('utf-8')}")

if __name__ == "__main__":
    src = "/Users/macbookair/.gemini/antigravity/brain/7e66f743-ac36-4b22-9484-93db6da48b0a/.user_uploaded/media_1794964475474_927c81a8.jpg"
    out = "/Users/macbookair/projects/live-walpaper/sample-wallpapers/mai_sakurajima_live.mp4"
    generate_live_wallpaper(src, out, duration=8.0, fps=60)
