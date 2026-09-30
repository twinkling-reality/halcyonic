import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { describe, test } from 'node:test';
import {
  clientSecret,
  fromBytes,
  modPow,
  multiplier,
  PAIRING_GROUP,
  PAIRING_IDENTITY,
  pad,
  privateKey,
  SrpClient,
  SrpServer,
  scramble,
  serverPublic,
  serverSecret,
  srpGroup,
  verifier,
} from './srp.ts';

const hex = (text: string) => BigInt(`0x${text.replace(/\s+/g, '')}`);
const bytes = (text: string) => Buffer.from(text.replace(/\s+/g, ''), 'hex');

/** RFC 5054 Appendix A, the 1024-bit group, which Appendix B's vectors use with SHA-1. */
const RFC_GROUP = srpGroup(
  `EEAF0AB9 ADB38DD6 9C33F80A FA8FC5E8 60726187 75FF3C0B 9EA2314C
   9C256576 D674DF74 96EA81D3 383B4813 D692C6E0 E0D5D8E2 50B98BE4
   8E495C1D 6089DAD1 5DC7D7B4 6154D6B6 CE8EF4AD 69B15D49 82559B29
   7BCF1885 C529F566 660E57EC 68EDBC3C 05726CC0 2FD4CBF4 976EAA9A
   FD5138FE 8376435B 9FC61D2F C0EB06E3`,
  2n,
  'sha1',
);

/** RFC 5054 Appendix B. */
const RFC = {
  I: Buffer.from('alice', 'utf8'),
  P: Buffer.from('password123', 'utf8'),
  s: bytes('BEB25379 D1A8581E B5A72767 3A2441EE'),
  k: hex('7556AA04 5AEF2CDD 07ABAF0F 665C3E81 8913186F'),
  x: hex('94B7555A ABE9127C C58CCF49 93DB6CF8 4D16C124'),
  v: hex(`7E273DE8 696FFC4F 4E337D05 B4B375BE B0DDE156 9E8FA00A 9886D812
          9BADA1F1 822223CA 1A605B53 0E379BA4 729FDC59 F105B478 7E5186F5
          C671085A 1447B52A 48CF1970 B4FB6F84 00BBF4CE BFBB1681 52E08AB5
          EA53D15C 1AFF87B2 B9DA6E04 E058AD51 CC72BFC9 033B564E 26480D78
          E955A5E2 9E7AB245 DB2BE315 E2099AFB`),
  a: hex(`60975527 035CF2AD 1989806F 0407210B C81EDC04 E2762A56 AFD529DD
          DA2D4393`),
  b: hex(`E487CB59 D31AC550 471E81F0 0F6928E0 1DDA08E9 74A004F4 9E61F5D1
          05284D20`),
  A: hex(`61D5E490 F6F1B795 47B0704C 436F523D D0E560F0 C64115BB 72557EC4
          4352E890 3211C046 92272D8B 2D1A5358 A2CF1B6E 0BFCF99F 921530EC
          8E393561 79EAE45E 42BA92AE ACED8251 71E1E8B9 AF6D9C03 E1327F44
          BE087EF0 6530E69F 66615261 EEF54073 CA11CF58 58F0EDFD FE15EFEA
          B349EF5D 76988A36 72FAC47B 0769447B`),
  B: hex(`BD0C6151 2C692C0C B6D041FA 01BB152D 4916A1E7 7AF46AE1 05393011
          BAF38964 DC46A067 0DD125B9 5A981652 236F99D9 B681CBF8 7837EC99
          6C6DA044 53728610 D0C6DDB5 8B318885 D7D82C7F 8DEB75CE 7BD4FBAA
          37089E6F 9C6059F3 88838E7A 00030B33 1EB76840 910440B1 B27AAEAE
          EB4012B7 D7665238 A8E3FB00 4B117B58`),
  u: hex('CE38B959 3487DA98 554ED47D 70A7AE5F 462EF019'),
  S: hex(`B0DC82BA BCF30674 AE450C02 87745E79 90A3381F 63B387AA F271A10D
          233861E3 59B48220 F7C4693C 9AE12B0A 6F67809F 0876E2D0 13800D6C
          41BB59B6 D5979B5C 00A172B4 A2A5903A 0BDCAF8A 709585EB 2AFAFA8F
          3499B200 210DCC1F 10EB3394 3CD67FC8 8A2F39A4 BE5BEC4E C0A3212D
          C346D7E4 74B29EDE 8A469FFE CA686E5A`),
};

describe('SRP-6a', () => {
  test("reproduces RFC 5054's test vectors on both sides", () => {
    assert.equal(multiplier(RFC_GROUP), RFC.k);
    assert.equal(privateKey(RFC_GROUP, RFC.I, RFC.P, RFC.s), RFC.x);
    assert.equal(verifier(RFC_GROUP, RFC.I, RFC.P, RFC.s), RFC.v);
    assert.equal(modPow(RFC_GROUP.g, RFC.a, RFC_GROUP.N), RFC.A);
    assert.equal(serverPublic(RFC_GROUP, RFC.v, RFC.b), RFC.B);
    assert.equal(scramble(RFC_GROUP, RFC.A, RFC.B), RFC.u);
    assert.equal(clientSecret(RFC_GROUP, RFC.B, RFC.x, RFC.a, RFC.u), RFC.S);
    assert.equal(serverSecret(RFC_GROUP, RFC.v, RFC.A, RFC.b, RFC.u), RFC.S);
  });

  test('both sides of the pairing group agree on the key only with the same code', () => {
    const salt = randomBytes(16);
    const code = Buffer.from('48210937', 'utf8');
    const server = SrpServer.create(PAIRING_GROUP, PAIRING_IDENTITY, code, salt);
    const client = new SrpClient(PAIRING_GROUP, PAIRING_IDENTITY, code, fromBytes(randomBytes(32)));
    const clientKey = client.sessionKey(salt, server.B);
    assert.ok(clientKey);
    assert.deepEqual(server.sessionKey(client.A), clientKey);

    const wrong = new SrpClient(
      PAIRING_GROUP,
      PAIRING_IDENTITY,
      Buffer.from('48210938', 'utf8'),
      fromBytes(randomBytes(32)),
    );
    const wrongKey = wrong.sessionKey(salt, server.B);
    assert.ok(wrongKey);
    assert.notDeepEqual(server.sessionKey(wrong.A), wrongKey);
  });

  test('attempts against one verifier each draw their own b, and agree with the client', () => {
    const salt = randomBytes(16);
    const code = Buffer.from('48210937', 'utf8');
    const v = verifier(PAIRING_GROUP, PAIRING_IDENTITY, code, salt);
    const first = SrpServer.forVerifier(PAIRING_GROUP, v);
    const second = SrpServer.forVerifier(PAIRING_GROUP, v);
    assert.notEqual(first.B, second.B);
    for (const server of [first, second]) {
      const client = new SrpClient(
        PAIRING_GROUP,
        PAIRING_IDENTITY,
        code,
        fromBytes(randomBytes(32)),
      );
      const key = client.sessionKey(salt, server.B);
      assert.ok(key);
      assert.deepEqual(server.sessionKey(client.A), key);
    }
  });

  test('the server refuses an A of 0 mod N, which would fix the key whatever the code', () => {
    const salt = randomBytes(16);
    const server = SrpServer.create(PAIRING_GROUP, PAIRING_IDENTITY, Buffer.from('1'), salt);
    for (const A of [0n, PAIRING_GROUP.N, 2n * PAIRING_GROUP.N, -1n]) {
      assert.equal(server.sessionKey(A), null, String(A));
    }
  });

  test('the client refuses a B of 0 mod N', () => {
    const client = new SrpClient(PAIRING_GROUP, PAIRING_IDENTITY, Buffer.from('1'), 7n);
    for (const B of [0n, PAIRING_GROUP.N]) {
      assert.equal(client.sessionKey(randomBytes(16), B), null, String(B));
    }
  });

  test('pads to the length of N, 384 bytes for the pairing group', () => {
    assert.equal(PAIRING_GROUP.length, 384);
    assert.equal(pad(PAIRING_GROUP, 5n).length, 384);
    assert.equal(pad(PAIRING_GROUP, 5n)[383], 5);
    assert.throws(() => pad(RFC_GROUP, PAIRING_GROUP.N));
  });
});
