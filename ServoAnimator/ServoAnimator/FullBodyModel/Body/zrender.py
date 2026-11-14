import math, numpy as np
from PIL import Image
def camera(az, el):
    a,e=math.radians(az),math.radians(el)
    f=-np.array([math.cos(e)*math.cos(a), math.cos(e)*math.sin(a), math.sin(e)])
    r=np.cross(f,[0,0,1.0]); r/=np.linalg.norm(r); u=np.cross(r,f); return f,r,u
def raster(T,C,cam,W,H,margin,ss=2,light=(0.75,-0.35,0.65),fit=None,bg=None):
    f,r,u=cam; L=np.array(light,float); L/=np.linalg.norm(L)
    X=T@r; Y=T@u; D=T@f
    if fit is None: fit=(X.min(),X.max(),Y.min(),Y.max())
    x0,x1,y0,y1=fit
    s=min((W-2*margin)/(x1-x0),(H-2*margin)/(y1-y0)); cx,cy=(x0+x1)/2,(y0+y1)/2
    Ws,Hs=W*ss,H*ss; PX=((X-cx)*s+W/2)*ss; PY=(H/2-(Y-cy)*s)*ss
    nrm=np.cross(T[:,1]-T[:,0],T[:,2]-T[:,0]); nl=np.linalg.norm(nrm,axis=1); nl[nl==0]=1; nrm/=nl[:,None]
    col=np.clip(C*(0.36+0.64*np.abs(nrm@L))[:,None],0,1)
    img=np.zeros((Hs,Ws,4)); zb=np.full((Hs,Ws),np.inf)
    if bg is not None: img[...,:3]=np.array(bg)/255; img[...,3]=1
    for i in range(len(T)):
        xs,ys,ds=PX[i],PY[i],D[i]
        bx0,bx1=int(max(0,math.floor(xs.min()))),int(min(Ws-1,math.ceil(xs.max())))
        by0,by1=int(max(0,math.floor(ys.min()))),int(min(Hs-1,math.ceil(ys.max())))
        if bx1<bx0 or by1<by0: continue
        den=(ys[1]-ys[2])*(xs[0]-xs[2])+(xs[2]-xs[1])*(ys[0]-ys[2])
        if abs(den)<1e-9: continue
        gx,gy=np.meshgrid(np.arange(bx0,bx1+1)+0.5,np.arange(by0,by1+1)+0.5)
        w0=((ys[1]-ys[2])*(gx-xs[2])+(xs[2]-xs[1])*(gy-ys[2]))/den
        w1=((ys[2]-ys[0])*(gx-xs[2])+(xs[0]-xs[2])*(gy-ys[2]))/den
        w2=1-w0-w1; m=(w0>=-1e-6)&(w1>=-1e-6)&(w2>=-1e-6)
        if not m.any(): continue
        z=w0*ds[0]+w1*ds[1]+w2*ds[2]; sub=zb[by0:by1+1,bx0:bx1+1]; upd=m&(z<sub); sub[upd]=z[upd]
        im=img[by0:by1+1,bx0:bx1+1]; im[upd,:3]=col[i]; im[upd,3]=1
    return Image.fromarray((img*255).astype(np.uint8),'RGBA').resize((W,H),Image.LANCZOS), fit


def raster_persp(T, C, eye, target, hfov_deg, W, H, ss=1, light=(0.55, 0.35, 0.75), bg=(40, 42, 46), near=0.05):
    """Pinhole-camera z-buffer render (perspective-correct depth via 1/z)."""
    eye = np.array(eye, float); f = np.array(target, float) - eye; f /= np.linalg.norm(f)
    r = np.cross(f, [0, 0, 1.0]); r /= np.linalg.norm(r); u = np.cross(r, f)
    L = np.array(light, float); L /= np.linalg.norm(L)
    # shade from the world triangles, then move to camera space
    nrm = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); nl = np.linalg.norm(nrm, axis=1); nl[nl == 0] = 1; nrm /= nl[:, None]
    col_all = np.clip(C * (0.42 + 0.58 * np.abs(nrm @ L))[:, None], 0, 1)
    P = T - eye
    V = np.stack([P @ r, P @ u, P @ f], axis=-1)            # (n, 3 vertices, [x, y, z])
    front = V[:, :, 2] > near
    whole = front.all(axis=1)
    tris, cols = [V[whole]], [col_all[whole]]
    # near-plane clipping for triangles that cross the camera plane
    extra, extra_c = [], []
    for i in np.where(front.any(axis=1) & ~whole)[0]:
        poly = []
        for k in range(3):
            a, b = V[i, k], V[i, (k + 1) % 3]
            ina, inb = a[2] > near, b[2] > near
            if ina: poly.append(a)
            if ina != inb:
                t = (near - a[2]) / (b[2] - a[2]); poly.append(a + (b - a) * t)
        for k in range(1, len(poly) - 1):
            extra.append([poly[0], poly[k], poly[k + 1]]); extra_c.append(col_all[i])
    if extra:
        tris.append(np.array(extra)); cols.append(np.array(extra_c))
    V = np.concatenate(tris); col = np.concatenate(cols)
    Xc, Yc, Zc = V[:, :, 0], V[:, :, 1], V[:, :, 2]
    Ws, Hs = W * ss, H * ss
    fpx = (Ws / 2) / math.tan(math.radians(hfov_deg) / 2)
    PX = Ws / 2 + fpx * Xc / Zc; PY = Hs / 2 - fpx * Yc / Zc
    T = V
    img = np.zeros((Hs, Ws, 3)); img[...] = np.array(bg) / 255
    wb = np.zeros((Hs, Ws))                          # stores 1/z; larger = nearer
    for i in range(len(T)):
        xs, ys, iz = PX[i], PY[i], 1.0 / Zc[i]
        bx0, bx1 = int(max(0, math.floor(xs.min()))), int(min(Ws - 1, math.ceil(xs.max())))
        by0, by1 = int(max(0, math.floor(ys.min()))), int(min(Hs - 1, math.ceil(ys.max())))
        if bx1 < bx0 or by1 < by0: continue
        den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(den) < 1e-9: continue
        gx, gy = np.meshgrid(np.arange(bx0, bx1 + 1) + 0.5, np.arange(by0, by1 + 1) + 0.5)
        w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
        w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
        w2 = 1 - w0 - w1; m = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not m.any(): continue
        w = w0 * iz[0] + w1 * iz[1] + w2 * iz[2]
        sub = wb[by0:by1 + 1, bx0:bx1 + 1]; upd = m & (w > sub); sub[upd] = w[upd]
        img[by0:by1 + 1, bx0:bx1 + 1][upd] = col[i]
    im = Image.fromarray((img * 255).astype(np.uint8), "RGB")
    return im.resize((W, H), Image.LANCZOS) if ss > 1 else im
