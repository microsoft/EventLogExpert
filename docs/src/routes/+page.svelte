<script lang="ts">
	import { onMount } from 'svelte';
	import * as config from '$lib/index'

	export let data

	onMount(() => {
		const tables = document.getElementsByTagName('table');
		for(let x = 0; x < tables.length;x++){
			tables[x].classList.add('table');
			tables[x].classList.add('table-striped');
		}
	});
</script>

<svelte:head>
	<title>Home - {config.title}</title>
</svelte:head>

<section>
	<div class="row align-items-center header">
		<div class="col-12 col-md-6">
				<h1>{config.title}</h1>
				<p>{config.description}</p>
		</div>
			
		<div class="col-12 col-md-6">
			<img class="w-100" alt="Screenshot showing a filtered combined view of three event logs" src="https://github.com/microsoft/EventLogExpert/blob/main/docs/.images/EventLogExpert-CombinedView.png?raw=true" />
		</div>
	</div>
</section>

<!-- Posts -->
<section class="my-5">
	<h1>Table of contents</h1>
	<hr />
	<ol class="list-group list-group-numbered">
		{#each data.docs as doc}
			<li class="list-group-item border-top-0 border-end-0 border-start-0">
				<a href="#{doc.slug}" >{doc.title}</a>
			</li>
		{/each}
	</ol>
</section>

<section class="my-5">
{#each data.docs as doc}
<section id={doc.slug} class="my-5">
	<h1>{doc.index}. {doc.title}</h1>
	<hr />
	<p class="ms-5">
		<svelte:component this={doc.content} />
	</p>
</section>
{/each}
</section>

<style>
	.header {
		min-height: 400px;
	}
</style>
