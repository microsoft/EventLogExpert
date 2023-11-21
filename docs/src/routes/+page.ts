import type { Docs } from '$lib/types'

export async function load() {
	let docs: Docs[] = []
	const paths = import.meta.glob('/src/docs/*.md', { eager: true })

	for (const path in paths) {
		const file = paths[path]
		const slug = path.split('/').at(-1)?.replace('.md', '')

		if (file && typeof file === 'object' && 'metadata' in file && slug) {
			const metadata = file.metadata as Omit<Docs, 'slug'>
			const content = file.default;
			const post = { ...metadata, slug, content } satisfies Docs
			console.log(post);
			post.published && docs.push(post)
		}
	}

	docs = docs.sort((first, second) =>
		(first.index > second.index) ? 1 : -1
	)
	return { docs }
}