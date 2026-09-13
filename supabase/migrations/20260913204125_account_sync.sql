-- One bounded encrypted profile per account. No recovery keys or plaintext credentials.
create table public.relay_accounts (
  user_id uuid primary key references auth.users(id) on delete cascade,
  revision bigint not null default 1 check (revision > 0),
  ciphertext text not null check (octet_length(ciphertext) between 40 and 2000040),
  updated_at timestamptz not null default now()
);
alter table public.relay_accounts enable row level security;
revoke all on public.relay_accounts from anon, authenticated;
grant select, insert, update on public.relay_accounts to authenticated;
create policy account_select on public.relay_accounts for select to authenticated
  using ((select auth.uid()) = user_id);
create policy account_insert on public.relay_accounts for insert to authenticated
  with check ((select auth.uid()) = user_id);
create policy account_update on public.relay_accounts for update to authenticated
  using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);

create table public.relay_delivery_claims (
  user_id uuid not null references auth.users(id) on delete cascade,
  event_hash text not null check (event_hash ~ '^[0-9a-f]{64}$'),
  claimed_at timestamptz not null default now(),
  primary key (user_id, event_hash)
);
alter table public.relay_delivery_claims enable row level security;
revoke all on public.relay_delivery_claims from anon, authenticated;
grant select, insert, delete on public.relay_delivery_claims to authenticated;
create policy claims_owner on public.relay_delivery_claims for all to authenticated
  using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);

-- Claims intentionally have no lease timeout: an ambiguous Discord send must not be retried.
-- A per-account transaction lock serializes capacity checks across devices.
create function public.relay_claim_delivery(event_hash text) returns boolean
language plpgsql security invoker set search_path = '' as $$
declare owner_id uuid := auth.uid(); inserted_count integer;
begin
  if owner_id is null then raise exception 'Sign in required'; end if;
  perform pg_advisory_xact_lock(hashtextextended(owner_id::text, 0));
  if exists(select 1 from public.relay_delivery_claims c where c.user_id = owner_id and c.event_hash = $1)
    then return false; end if;
  if (select count(*) from public.relay_delivery_claims c where c.user_id = owner_id) >= 10000
    then raise exception 'Account delivery capacity reached'; end if;
  insert into public.relay_delivery_claims(user_id, event_hash) values(owner_id, $1) on conflict do nothing;
  get diagnostics inserted_count = row_count;
  return inserted_count = 1;
end;
$$;
revoke execute on function public.relay_claim_delivery(text) from public, anon;
grant execute on function public.relay_claim_delivery(text) to authenticated;
