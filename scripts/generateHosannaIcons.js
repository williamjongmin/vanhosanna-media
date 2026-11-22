// Produce native icons from the church mark using Node built-ins only.
const { readFileSync, writeFileSync } = require("fs")
const { join } = require("path")
const { deflateSync } = require("zlib")
const root = join(__dirname, "..")
const svg = readFileSync(join(root, "public", "hosanna-logo.svg"), "utf8")
const shapes = []
for (const match of svg.matchAll(/<path fill="(#[0-9a-fA-F]{6})" d="([^"]+)"/g)) {
    const color = [parseInt(match[1].slice(1, 3), 16), parseInt(match[1].slice(3, 5), 16), parseInt(match[1].slice(5, 7), 16), 255]
    for (const part of match[2].matchAll(/M([^Z]+)Z/g)) {
        const xy = part[1].match(/-?\d+(?:\.\d+)?/g).map(Number)
        const points = []
        for (let k = 0; k < xy.length; k += 2) points.push([xy[k], xy[k + 1]])
        shapes.push({ color, points })
    }
}
if (!shapes.length) throw Error("No church logo paths found")
function crc32(buffer) {
    let crc = 0xffffffff
    for (const b of buffer) {
        crc ^= b
        for (let k = 0; k < 8; k++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1))
    }
    return (crc ^ 0xffffffff) >>> 0
}
function chunk(type, data) {
    const name = Buffer.from(type)
    const head = Buffer.alloc(4), tail = Buffer.alloc(4)
    head.writeUInt32BE(data.length)
    tail.writeUInt32BE(crc32(Buffer.concat([name, data])))
    return Buffer.concat([head, name, data, tail])
}
function render(size) {
    const n = size * 2
    const rgba = Buffer.alloc(n * n * 4)
    const scale = n * 0.75 / 212
    const ox = (n - 212 * scale) / 2 + 4 * scale
    const oy = (n - 158 * scale) / 2 + 4 * scale
    const inset = n / 64, radius = n * 0.21
    for (let y = 0; y < n; y++) {
        for (let x = 0; x < n; x++) {
            const dx = Math.max(inset + radius - x, x - (n - inset - radius), 0)
            const dy = Math.max(inset + radius - y, y - (n - inset - radius), 0)
            if (dx * dx + dy * dy <= radius * radius) rgba.set([21, 30, 24, 255], (y * n + x) * 4)
        }
    }
    for (const { color, points } of shapes) {
        const p = points.map(([x, y]) => [x * scale + ox, y * scale + oy])
        const minY = Math.max(0, Math.floor(Math.min(...p.map(v => v[1]))))
        const maxY = Math.min(n - 1, Math.ceil(Math.max(...p.map(v => v[1]))))
        for (let y = minY; y <= maxY; y++) {
            const intersections = []
            for (let i = 0; i < p.length; i++) {
                const [ax, ay] = p[i], [bx, by] = p[(i + 1) % p.length]
                if ((ay > y + 0.5) !== (by > y + 0.5)) intersections.push(ax + (y + 0.5 - ay) * (bx - ax) / (by - ay))
            }
            intersections.sort((a, b) => a - b)
            for (let i = 0; i + 1 < intersections.length; i += 2) {
                for (let x = Math.max(0, Math.ceil(intersections[i] - 0.5)); x < Math.min(n, Math.ceil(intersections[i + 1] - 0.5)); x++) rgba.set(color, (y * n + x) * 4)
            }
        }
    }
    const stride = size * 4 + 1
    const raw = Buffer.alloc(stride * size)
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
        const indices = [((2*y)*n+2*x)*4, ((2*y)*n+2*x+1)*4, ((2*y+1)*n+2*x)*4, ((2*y+1)*n+2*x+1)*4]
        const alpha = indices.reduce((a,i) => a + rgba[i+3], 0)
        const o = y * stride + 1 + x * 4
        for (let c = 0; c < 3; c++) raw[o+c] = alpha ? Math.round(indices.reduce((a,i) => a + rgba[i+c]*rgba[i+3], 0)/alpha) : 0
        raw[o+3] = Math.round(alpha/4)
    }
    const ihdr = Buffer.alloc(13)
    ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4); ihdr[8] = 8; ihdr[9] = 6
    return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))])
}
const cache = new Map()
function png(size) {
    if (!cache.has(size)) cache.set(size, render(size))
    return cache.get(size)
}
for (const target of ["public/icon.png", "src/server/icon.png", "scripts/snap/gui/icon.png"]) writeFileSync(join(root, target), png(512))
const sizes = [16, 32, 48, 64, 128, 256]
const header = Buffer.alloc(6 + 16 * sizes.length)
header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4)
let offset = header.length
const images = sizes.map((n, i) => {
    const image = png(n), p = 6 + i * 16
    header[p] = header[p+1] = n === 256 ? 0 : n
    header.writeUInt16LE(1, p+4); header.writeUInt16LE(32, p+6)
    header.writeUInt32LE(image.length, p+8); header.writeUInt32LE(offset, p+12)
    offset += image.length
    return image
})
writeFileSync(join(root, "public/icon.ico"), Buffer.concat([header, ...images]))
const chunks = [["ic07",128],["ic08",256],["ic09",512],["ic10",1024]].map(([type,n]) => {
    const image = png(n), head = Buffer.alloc(8)
    head.write(type); head.writeUInt32BE(image.length+8, 4)
    return Buffer.concat([head, image])
})
const icns = Buffer.alloc(8)
icns.write("icns"); icns.writeUInt32BE(8+chunks.reduce((a,b) => a+b.length,0),4)
writeFileSync(join(root,"public/icon.icns"), Buffer.concat([icns,...chunks]))
console.log("HosannaMedia icons generated.")
