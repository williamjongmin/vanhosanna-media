const sharp = require("sharp")
const { readFileSync, writeFileSync } = require("fs")
const { join } = require("path")

const root = join(__dirname, "..")
const logo = readFileSync(join(root, "public", "hosanna-logo.svg"))
async function main() {
    const size = 512
    const mark = await sharp(logo).resize(384, 384, { fit: "inside" }).png().toBuffer()
    const background = Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512"><rect x="8" y="8" width="496" height="496" rx="108" fill="#151E18"/></svg>')
    const png = await sharp(background).composite([{ input: mark, gravity: "centre" }]).png().toBuffer()
    const targets = ["public/icon.png", "src/server/icon.png", "scripts/snap/gui/icon.png"]
    for (const target of targets) writeFileSync(join(root, target), png)

    // Windows ICO embeds PNG images at standard desktop sizes.
    const sizes = [16, 32, 48, 64, 128, 256]
    const images = await Promise.all(sizes.map(n => sharp(png).resize(n, n).png().toBuffer()))
    const header = Buffer.alloc(6 + 16 * sizes.length)
    header.writeUInt16LE(1, 2)
    header.writeUInt16LE(sizes.length, 4)
    let offset = header.length
    images.forEach((image, i) => {
        const p = 6 + i * 16
        header[p] = header[p + 1] = sizes[i] === 256 ? 0 : sizes[i]
        header.writeUInt16LE(1, p + 4)
        header.writeUInt16LE(32, p + 6)
        header.writeUInt32LE(image.length, p + 8)
        header.writeUInt32LE(offset, p + 12)
        offset += image.length
    })
    writeFileSync(join(root, "public/icon.ico"), Buffer.concat([header, ...images]))

    // macOS ICNS contains the larger PNG icon representations.
    const chunks = await Promise.all([["ic07", 128], ["ic08", 256], ["ic09", 512], ["ic10", 1024]].map(async ([type, n]) => {
        const image = await sharp(png).resize(n, n).png().toBuffer()
        const head = Buffer.alloc(8)
        head.write(type, 0)
        head.writeUInt32BE(image.length + 8, 4)
        return Buffer.concat([head, image])
    }))
    const icnsHead = Buffer.alloc(8)
    icnsHead.write("icns")
    icnsHead.writeUInt32BE(8 + chunks.reduce((sum, b) => sum + b.length, 0), 4)
    writeFileSync(join(root, "public/icon.icns"), Buffer.concat([icnsHead, ...chunks]))
    console.log("HosannaMedia icons generated.")
}
main().catch(err => {
    console.error(err)
    process.exitCode = 1
})
